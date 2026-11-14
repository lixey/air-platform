using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalChildBuilder
{
  public static async Task<HashSet<string>> CollectChildReferenceServicesAsync(
    ProposalBuildContext ctx,
    IReadOnlyList<AgentEditableChildDescriptor> editableChildren,
    bool isEdit,
    long? recordId,
    string parentServicePath,
    CancellationToken ct = default)
  {
    var refServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var childDesc in editableChildren)
    {
      await using var scope = AgentModuleScope.OpenForService(ctx.Services, childDesc.ChildFormType, ctx.Db, ctx.App);
      if (ModuleCatalog.Instance.CreateServiceInstance(childDesc.ChildFormType, scope.Db, scope.App) is not IFormService childForm)
      {
        continue;
      }

      childForm.SetRequest(new FormRequest { ParentId = isEdit ? recordId : null, ParentService = parentServicePath });
      var (cEls, _) = await childForm.GetElementsMetadataAsync(ct);
      foreach (var cel in cEls.Where(AgentFeatureRegistry.IsEditableElement))
      {
        if (cel.Model is FormSelectModel cs && !string.IsNullOrEmpty(cs.ReferenceService))
        {
          refServices.Add(cs.ReferenceService);
        }
      }
    }

    return refServices;
  }

  public static async Task<(List<AgentProposalChildGroupDto> DisplayGroups, List<StagedChildGroupPayload> StagedGroups)> BuildChildGroupsAsync(
    ProposalBuildContext ctx,
    ProposalDraftState draft,
    IReadOnlyList<ChildGridBuilder> allChildren,
    IReadOnlyList<AgentEditableChildDescriptor> editableChildren,
    CancellationToken ct)
  {
    var requestedInputs = ExtractRequestedChildInputs(draft.Args, draft.ValuesSource, allChildren, editableChildren);
    var displayGroups = new List<AgentProposalChildGroupDto>();
    var stagedGroups = new List<StagedChildGroupPayload>();

    foreach (var (childDesc, mode, rowsProp) in requestedInputs)
    {
      await using var scope = AgentModuleScope.OpenForService(ctx.Services, childDesc.ChildFormType, ctx.Db, ctx.App);
      var childCtx = ctx.WithScope(scope.Db, scope.App);
      var childForm = ModuleCatalog.Instance.CreateServiceInstance(childDesc.ChildFormType, scope.Db, scope.App) as IFormService
        ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);

      var allHaveIds = rowsProp.GetArrayLength() > 0 &&
                       rowsProp.EnumerateArray().All(el => el.ValueKind == JsonValueKind.Object && TryGetRecordId(el) is > 0);
      childForm.SetRequest(new FormRequest { ParentId = draft.IsEdit ? draft.RecordId : null, ParentService = draft.Target.ServicePath });
      var (rawElements, _) = await childForm.GetElementsMetadataAsync(ct);
      if (draft.IsEdit && !allHaveIds && childForm.IsFormReadOnly)
      {
        throw new InvalidOperationException(ctx.Localizer["FormReadOnly"]);
      }

      var childEditableElements = rawElements.Where(AgentFeatureRegistry.IsEditableElement).ToList();

      if (draft.IsEdit &&
          ModuleCatalog.Instance.CreateServiceInstance(childDesc.ChildGridType, scope.Db, scope.App) is IGridService childGrid &&
          string.Equals(mode, AgentProposalChildMode.Replace, StringComparison.OrdinalIgnoreCase))
      {
        if (!await childGrid.BindParentAsync(draft.Target.ServicePath, draft.RecordId, ct))
        {
          throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
        }

        if (!childGrid.CanDelete() && !allHaveIds)
        {
          var countDto = await childGrid.GetCountAsync(new GridRequest { ParentService = draft.Target.ServicePath, ParentId = draft.RecordId }, ct);
          if (countDto.Total > 0)
          {
            throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
          }
        }
      }

      var childDefaults = (await childForm.PrepareDraftAsync(
        recordId: null,
        parentId: draft.IsEdit ? draft.RecordId : null,
        parentService: draft.IsEdit ? draft.Target.ServicePath : null,
        ct: ct)).Values;
      var rowParseScope = new ChildRowParseScope(childCtx, draft, childForm, childDefaults, childEditableElements);
      var displayRows = new List<AgentProposalChildRowDto>();
      var rawRows = new List<Dictionary<string, object?>>();
      var seenColumnKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      foreach (var item in rowsProp.EnumerateArray())
      {
        if (item.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        var rowResult = await AgentProposalChildRowParser.ParseSingleChildRowAsync(rowParseScope, item, ct);
        if (rowResult == null)
        {
          continue;
        }

        var (rowDto, rowDict, rowPendingRefs) = rowResult.Value;
        foreach (var colKey in rowDto.Cells.Keys)
        {
          seenColumnKeys.Add(colKey);
        }

        displayRows.Add(rowDto);
        rawRows.Add(rowDict);
        var addedRowIdx = rawRows.Count - 1;
        foreach (var pr in rowPendingRefs)
        {
          pr.ChildKey = childDesc.ChildKey;
          pr.LineIndex = addedRowIdx;
          draft.NewReferences.Add(pr);
        }
      }

      ApplyChildSumMappings(childDesc.Builder, rawRows, draft.FormValues, draft.ExplicitlyProvided);

      var columns = childEditableElements
        .Where(cel => seenColumnKeys.Contains(cel.Path))
        .Select(cel => new AgentProposalChildColumnDto
        {
          Key = cel.Path,
          Label = cel.Label ?? cel.Path,
          IsNumeric = cel.Type == UiFieldType.Number || cel.Model is FormMoneyModel or FormPercentModel
        })
        .ToList();

      if (displayRows.Count > 0)
      {
        displayGroups.Add(new AgentProposalChildGroupDto
        {
          ChildKey = childDesc.ChildKey,
          ChildService = childDesc.ChildGridService,
          Title = childDesc.Title,
          Mode = mode,
          Columns = columns,
          Rows = displayRows
        });
      }

      stagedGroups.Add(new StagedChildGroupPayload
      {
        ChildKey = childDesc.ChildKey,
        ChildGridService = childDesc.ChildGridService,
        ChildFormService = childDesc.ChildFormService,
        Mode = mode,
        HasUpdate = draft.IsEdit || rawRows.Count > 0,
        Rows = rawRows
      });
    }

    return (displayGroups, stagedGroups);
  }

  private static List<(AgentEditableChildDescriptor Descriptor, string Mode, JsonElement Rows)> ExtractRequestedChildInputs(
    JsonElement args,
    JsonElement valuesSource,
    IReadOnlyList<ChildGridBuilder> allChildren,
    IReadOnlyList<AgentEditableChildDescriptor> editableChildren)
  {
    var result = new List<(AgentEditableChildDescriptor, string, JsonElement)>();

    if (TryGetPropertyIgnoreCase(args, "children", out var childrenEl) ||
        !valuesSource.Equals(args) && TryGetPropertyIgnoreCase(valuesSource, "children", out childrenEl))
    {
      if (childrenEl.ValueKind == JsonValueKind.Array)
      {
        foreach (var groupEl in childrenEl.EnumerateArray())
        {
          if (groupEl.ValueKind != JsonValueKind.Object)
          {
            continue;
          }

          var rawKey = TryGetString(groupEl, "childKey")
            ?? TryGetString(groupEl, "key")
            ?? TryGetString(groupEl, "childService")
            ?? TryGetString(groupEl, "service");
          var desc = MatchEditableChildDescriptor(rawKey, allChildren, editableChildren);
          var mode = NormalizeChildMode(TryGetString(groupEl, "mode"));
          if (TryGetPropertyIgnoreCase(groupEl, "rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
          {
            result.Add((desc, mode, rowsEl));
          }
        }
      }
      else if (childrenEl.ValueKind == JsonValueKind.Object)
      {
        foreach (var prop in childrenEl.EnumerateObject())
        {
          var desc = MatchEditableChildDescriptor(prop.Name, allChildren, editableChildren);
          if (prop.Value.ValueKind == JsonValueKind.Array)
          {
            result.Add((desc, AgentProposalChildMode.Replace, prop.Value));
          }
          else if (prop.Value.ValueKind == JsonValueKind.Object &&
                   TryGetPropertyIgnoreCase(prop.Value, "rows", out var rowsEl) &&
                   rowsEl.ValueKind == JsonValueKind.Array)
          {
            result.Add((desc, NormalizeChildMode(TryGetString(prop.Value, "mode")), rowsEl));
          }
        }
      }
    }

    if (result.Count == 0 &&
        (TryGetPropertyIgnoreCase(args, "lines", out var linesEl) ||
         !valuesSource.Equals(args) && TryGetPropertyIgnoreCase(valuesSource, "lines", out linesEl)) &&
        linesEl.ValueKind == JsonValueKind.Array)
    {
      if (editableChildren.Count == 0)
      {
        if (allChildren.Any(c => !c.IsFiles))
        {
          throw new InvalidOperationException("Child grid is read-only or not enabled for AI mutations.");
        }
      }
      else
      {
        var rawMode = TryGetString(args, "linesMode") ?? TryGetString(valuesSource, "linesMode");
        result.Add((editableChildren[0], NormalizeChildMode(rawMode), linesEl));
      }
    }

    return result;
  }

  private static AgentEditableChildDescriptor MatchEditableChildDescriptor(
    string? rawKey,
    IReadOnlyList<ChildGridBuilder> allChildren,
    IReadOnlyList<AgentEditableChildDescriptor> editableChildren)
  {
    if (string.IsNullOrWhiteSpace(rawKey))
    {
      return editableChildren.Count == 1
        ? editableChildren[0]
        : throw new InvalidOperationException("Specify 'childKey' when staging child rows on a multi-child form.");
    }

    var key = rawKey.Trim();
    var normKey = AgentFeatureRegistry.NormalizeServiceKey(key);
    var matched = editableChildren.FirstOrDefault(c =>
      string.Equals(c.ChildKey, key, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(c.ChildGridService, key, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(c.ChildFormService, key, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(AgentFeatureRegistry.NormalizeServiceKey(c.ChildKey), normKey, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(AgentFeatureRegistry.NormalizeServiceKey(c.ChildGridService), normKey, StringComparison.OrdinalIgnoreCase));

    if (matched != null)
    {
      return matched;
    }

    var declaredNonFile = allChildren.FirstOrDefault(c =>
      !c.IsFiles &&
      (string.Equals(c.DeclaredKey, key, StringComparison.OrdinalIgnoreCase) ||
       string.Equals(c.ServicePath, key, StringComparison.OrdinalIgnoreCase)));
    if (declaredNonFile != null)
    {
      throw new InvalidOperationException($"Child grid '{key}' is read-only or not enabled for AI mutations.");
    }

    throw new InvalidOperationException($"Unknown child grid '{key}'.");
  }

  private static string NormalizeChildMode(string? rawMode) =>
    string.Equals(rawMode?.Trim(), AgentProposalChildMode.Append, StringComparison.OrdinalIgnoreCase)
      ? AgentProposalChildMode.Append
      : AgentProposalChildMode.Replace;

  private static void ApplyChildSumMappings(
    ChildGridBuilder childBuilder,
    IReadOnlyList<Dictionary<string, object?>> rawRows,
    Dictionary<string, object?> parentFormValues,
    HashSet<string> explicitlyProvidedParentFields)
  {
    if (rawRows.Count == 0 || childBuilder.SumMappings.Count == 0)
    {
      return;
    }

    foreach (var mapping in childBuilder.SumMappings)
    {
      if (string.IsNullOrWhiteSpace(mapping.ParentFieldPath) ||
          string.IsNullOrWhiteSpace(mapping.ChildFieldPath) ||
          explicitlyProvidedParentFields.Contains(mapping.ParentFieldPath))
      {
        continue;
      }

      var computedSum = 0m;
      foreach (var dict in rawRows)
      {
        if (dict.TryGetValue(mapping.ChildFieldPath, out var cellVal) && TryConvertToDecimal(cellVal, out var decVal))
        {
          computedSum += decVal;
        }
      }

      if (computedSum != 0m)
      {
        var baseTotal = parentFormValues.TryGetValue(mapping.ParentFieldPath, out var curVal) &&
                        TryConvertToDecimal(curVal, out var existingTotal)
          ? existingTotal
          : 0m;
        parentFormValues[mapping.ParentFieldPath] = baseTotal + computedSum;
      }
    }
  }
}
