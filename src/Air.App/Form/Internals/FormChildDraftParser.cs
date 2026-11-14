namespace Platform.App.Form.Internals;

internal static class FormChildDraftParser
{
  public static async Task<List<(ChildGridBuilder Builder, Type ChildGridType, string ChildGridService, Type ChildFormType, string ChildFormService, string ChildKey, string Title, IReadOnlyList<FormElementDto> EditableElements)>> DiscoverEditableChildrenAsync(
    IFormService parentForm,
    IReadOnlyList<ChildGridBuilder> allChildren,
    bool isEdit,
    long? recordId,
    PlatformDbContext db,
    PlatformAppService? app,
    Func<Feature, Feature, bool>? childPermissionFilter,
    CancellationToken ct)
  {
    var list = new List<(ChildGridBuilder, Type, string, Type, string, string, string, IReadOnlyList<FormElementDto>)>();
    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var child in allChildren)
    {
      if (child.IsFiles || child.IsReadOnly || child.ServiceType == null)
      {
        continue;
      }

      if (ModuleCatalog.Instance.CreateServiceInstance(child.ServiceType, db, app) is not Grid.IGridService childGrid ||
          childGrid.Feature.IsReadOnly ||
          childPermissionFilter != null && !childPermissionFilter(childGrid.Feature, parentForm.Feature) ||
          !ServiceEndpointsExtensions.CanView(childGrid.Feature, app?.User) ||
          !(db.TenantContext.IsSystemScope || ServiceEndpointsExtensions.CanEdit(childGrid.Feature, app?.User)))
      {
        continue;
      }

      var declaredForms = await childGrid.GetDeclaredFormsAsync(ct);
      var formDesc = declaredForms.FirstOrDefault();
      if (formDesc == null)
      {
        continue;
      }

      if (ModuleCatalog.Instance.CreateServiceInstance(formDesc.FormServiceType, db, app) is not IFormService childForm ||
          childForm.Feature.IsReadOnly ||
          childPermissionFilter != null && !childPermissionFilter(childForm.Feature, parentForm.Feature) ||
          !(db.TenantContext.IsSystemScope || ServiceEndpointsExtensions.CanEdit(childForm.Feature, app?.User)))
      {
        continue;
      }

      childForm.SetRequest(new FormRequest { ParentId = isEdit ? recordId : null, ParentService = parentForm.Service });
      var (cEls, _) = await childForm.GetElementsMetadataAsync(ct);
      var editable = cEls.Where(FormReferenceLookupResolver.IsEditableElement).ToList();

      var key = !string.IsNullOrWhiteSpace(child.DeclaredKey) ? child.DeclaredKey : child.ServicePath;
      if (!seenKeys.Add(key))
      {
        continue;
      }

      var rawTitle = !string.IsNullOrWhiteSpace(child.DeclaredLabel)
        ? child.DeclaredLabel
        : (childGrid.Feature.Name ?? key);
      var title = Localizer.Instance.For(childGrid.Feature.Module)[rawTitle];
      list.Add((child, child.ServiceType, child.ServicePath, formDesc.FormServiceType, formDesc.ServicePath, key, title, editable));
    }

    return list;
  }

  public static async Task<(List<FormChildDraftGroupDto> Groups, List<MessageDto> Messages, bool AppliedAnySumTo)> BuildChildDraftsAsync<TEntity>(
    FormService<TEntity> parentForm,
    IReadOnlyList<ChildGridBuilder> allChildren,
    IReadOnlyList<(ChildGridBuilder Builder, Type ChildGridType, string ChildGridService, Type ChildFormType, string ChildFormService, string ChildKey, string Title, IReadOnlyList<FormElementDto> EditableElements)> editableChildren,
    JsonElement argsObj,
    JsonElement valuesObj,
    bool isEdit,
    long? recordId,
    Dictionary<string, object?> parentValues,
    HashSet<string> parentExplicitFields,
    List<FormUnresolvedReferenceDto> unresolvedRefs,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Func<FormElementDto, string, string?>? createFormResolver,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var requested = ExtractRequestedChildInputs(argsObj, valuesObj, allChildren, editableChildren, localizer);
    var groups = new List<FormChildDraftGroupDto>(requested.Count);
    var childMessages = new List<MessageDto>();
    var appliedAnySumTo = false;

    foreach (var (desc, mode, rowsEl) in requested)
    {
      var childForm = ModuleCatalog.Instance.CreateServiceInstance(desc.ChildFormType, db, app) as IFormService
        ?? throw FormCompositeDraftBuilder.Refuse(localizer["MutationRefused"]);
      var allHaveIds = rowsEl.GetArrayLength() > 0 &&
                       rowsEl.EnumerateArray().All(el => el.ValueKind == JsonValueKind.Object && TryGetRecordId(el) is > 0);

      childForm.SetRequest(new FormRequest { ParentId = isEdit ? recordId : null, ParentService = parentForm.Service });
      _ = await childForm.GetElementsMetadataAsync(ct);
      if (isEdit && !allHaveIds && childForm.IsFormReadOnly)
      {
        throw FormCompositeDraftBuilder.Refuse(localizer["FormReadOnly"]);
      }

      var isAppend = string.Equals(mode, "append", StringComparison.OrdinalIgnoreCase);
      if (isEdit && !isAppend &&
          ModuleCatalog.Instance.CreateServiceInstance(desc.ChildGridType, db, app) is Grid.IGridService childGrid)
      {
        if (!await childGrid.BindParentAsync(parentForm.Service, recordId, ct))
        {
          throw FormCompositeDraftBuilder.Refuse(localizer["MutationRefused"]);
        }

        if (!childGrid.CanDelete() && !allHaveIds)
        {
          var countDto = await childGrid.GetCountAsync(new GridRequest { ParentService = parentForm.Service, ParentId = recordId }, ct);
          if (countDto.Total > 0)
          {
            throw FormCompositeDraftBuilder.Refuse(localizer["MutationRefused"]);
          }
        }
      }

      var childDefaults = (await childForm.PrepareDraftAsync(
        recordId: null,
        parentId: isEdit ? recordId : null,
        parentService: isEdit ? parentForm.Service : null,
        ct: ct)).Values;

      var draftRows = new List<FormChildDraftRowDto>();
      var rawRowsForSum = new List<Dictionary<string, object?>>();

      foreach (var item in rowsEl.EnumerateArray())
      {
        if (item.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        var itemRowId = isEdit ? TryGetRecordId(item) : null;
        Dictionary<string, object?> baseRowValues;
        if (itemRowId is > 0)
        {
          baseRowValues = (await childForm.PrepareDraftAsync(
            recordId: itemRowId.Value,
            parentId: recordId,
            parentService: parentForm.Service,
            ct: ct)).Values;
          if (childForm.IsFormReadOnly)
          {
            throw FormCompositeDraftBuilder.Refuse(localizer["FormReadOnly"]);
          }
        }
        else
        {
          baseRowValues = childDefaults;
        }

        var rowValues = FormDraftEngine.CloneDraftValues(baseRowValues);
        if (itemRowId is > 0)
        {
          rowValues["id"] = itemRowId.Value;
        }

        var rowExplicit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rowPending = new List<FormUnresolvedReferenceDto>();

        foreach (var cel in desc.EditableElements)
        {
          var cSelect = cel.Model as FormSelectModel;
          if (cSelect != null && !string.IsNullOrEmpty(cSelect.ReferenceService))
          {
            await ParseChildReferenceCellAsync(
              item,
              cel,
              cSelect,
              desc.EditableElements,
              parentValues,
              unresolvedRefs,
              rowValues,
              rowExplicit,
              rowLabels,
              rowPending,
              dictionaries,
              desc.ChildFormType,
              createFormResolver,
              db,
              app,
              localizer,
              ct);
            continue;
          }

          if (FormDraftEngine.TryGetJsonPropertyIgnoreCase(item, cel.Path, out var rawCell) &&
              rawCell.ValueKind != JsonValueKind.Undefined)
          {
            rowExplicit.Add(cel.Path);
            rowValues[cel.Path] = FormReferenceLookupResolver.ConvertJsonScalar(rawCell, cel.Type, cSelect);
          }
        }

        if (rowExplicit.Count == 0 && itemRowId is not > 0)
        {
          continue;
        }

        var rowPendingPaths = rowPending.Select(r => r.FieldPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rowOverrides = rowExplicit
          .Where(k => !rowPendingPaths.Contains(k))
          .ToDictionary(k => k, k => rowValues.GetValueOrDefault(k), StringComparer.OrdinalIgnoreCase);

        var rowDraft = await childForm.PrepareDraftAsync(
          recordId: itemRowId,
          overrides: rowOverrides,
          pendingReferencePaths: rowPendingPaths,
          parentId: isEdit ? recordId : null,
          parentService: parentForm.Service,
          ct: ct);

        foreach (var (k, v) in rowDraft.Values)
        {
          if (!rowPendingPaths.Contains(k) && v != null &&
              (!rowExplicit.Contains(k) || !rowValues.TryGetValue(k, out var curV) || curV == null ||
               Platform.App.Internals.FilterNodeEvaluator.TryConvertToDecimal(curV, out var curDec) && curDec == 0m))
          {
            rowValues[k] = v;
          }
        }

        var addedIdx = draftRows.Count;
        foreach (var pr in rowPending)
        {
          unresolvedRefs.Add(new FormUnresolvedReferenceDto
          {
            FieldPath = pr.FieldPath,
            ChildKey = desc.ChildKey,
            RowIndex = addedIdx,
            IsMultiple = pr.IsMultiple,
            SymbolicRefKey = pr.SymbolicRefKey,
            RawName = pr.RawName,
            ReferenceService = pr.ReferenceService,
            CreateFormService = pr.CreateFormService,
            ExtraValues = pr.ExtraValues
          });
        }

        var formattedRowMsgs = rowDraft.Messages
          .Select(m => new MessageDto
          {
            Code = m.Code,
            Path = m.Path,
            RecordId = m.RecordId,
            Type = m.Type,
            Detail = $"{desc.Title} #{addedIdx + 1}: {m.Detail ?? m.Title}"
          })
          .ToList();
        childMessages.AddRange(formattedRowMsgs);

        draftRows.Add(new FormChildDraftRowDto
        {
          RecordId = itemRowId,
          Ver = rowDraft.Ver,
          Values = rowValues,
          ExplicitFields = rowExplicit,
          ReferenceLabels = rowLabels,
          Messages = formattedRowMsgs
        });
        rawRowsForSum.Add(rowValues);
      }

      if (desc.Builder.SumMappings.Count > 0)
      {
        await ChildSumEvaluator.ApplyCompositeDraftSumsAsync<TEntity>(
          db,
          app,
          recordId,
          desc.Builder,
          isAppend,
          rawRowsForSum,
          parentValues,
          parentExplicitFields,
          ct);
        appliedAnySumTo = true;
      }

      groups.Add(new FormChildDraftGroupDto
      {
        ChildKey = desc.ChildKey,
        ChildGridService = desc.ChildGridService,
        ChildFormService = desc.ChildFormService,
        Title = desc.Title,
        Mode = mode,
        HasUpdate = isEdit || draftRows.Count > 0,
        Elements = desc.EditableElements,
        Rows = draftRows
      });
    }

    return (groups, childMessages, appliedAnySumTo);
  }

  private static async Task ParseChildReferenceCellAsync(
    JsonElement item,
    FormElementDto cel,
    FormSelectModel cSelect,
    IReadOnlyList<FormElementDto> childEditableElements,
    Dictionary<string, object?> parentValues,
    List<FormUnresolvedReferenceDto> parentUnresolved,
    Dictionary<string, object?> rowValues,
    HashSet<string> rowExplicit,
    Dictionary<string, string> rowLabels,
    List<FormUnresolvedReferenceDto> rowPending,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type referrerType,
    Func<FormElementDto, string, string?>? createFormResolver,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    CancellationToken ct)
  {
    var hasCell = FormDraftEngine.TryGetJsonPropertyIgnoreCase(item, cel.Path, out var rawCell);
    if (!hasCell && cel.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && cel.Path.Length > 2)
    {
      var baseName = cel.Path[..^2];
      var allowNameFallback = !childEditableElements.Any(e => string.Equals(e.Path, "name", StringComparison.OrdinalIgnoreCase)) &&
                              ReferenceEquals(cel, childEditableElements.FirstOrDefault(e => e.Model is FormSelectModel));
      var aliases = allowNameFallback ? new[] { $"{baseName}Name", baseName, "name" } : new[] { $"{baseName}Name", baseName };
      foreach (var alias in aliases)
      {
        if (FormDraftEngine.TryGetJsonPropertyIgnoreCase(item, alias, out rawCell))
        {
          hasCell = true;
          break;
        }
      }
    }

    long? resolvedId = null;
    string? unresolvedName = null;
    var handledBySymRef = false;

    if (hasCell)
    {
      rowExplicit.Add(cel.Path);
      if (FormReferenceLookupResolver.TryExtractSymbolicRefKey(rawCell, out var symKey))
      {
        handledBySymRef = true;
        rowPending.Add(new FormUnresolvedReferenceDto
        {
          FieldPath = cel.Path,
          SymbolicRefKey = symKey,
          ReferenceService = cSelect.ReferenceService
        });
      }
      else
      {
        var createForm = createFormResolver?.Invoke(cel, cSelect.ReferenceService!);
        (resolvedId, unresolvedName, var extraObj) = await FormReferenceLookupResolver.ResolveReferenceValueAsync(
          rawCell,
          cSelect.ReferenceService!,
          cSelect.ReferenceFilter,
          allowFuzzy: true,
          allowWrongKindFallback: false,
          dictionaries,
          referrerType,
          db,
          app,
          ct,
          cSelect.ReferenceLabelPaths);

        if (!resolvedId.HasValue && !string.IsNullOrWhiteSpace(unresolvedName))
        {
          var extra = Platform.App.Internals.FilterNodeEvaluator.ExtractEqualityValues(cSelect.ReferenceFilter);
          if (extraObj != null)
          {
            foreach (var (k, v) in extraObj)
            {
              extra[k] = v;
            }
          }

          rowPending.Add(new FormUnresolvedReferenceDto
          {
            FieldPath = cel.Path,
            RawName = unresolvedName,
            ReferenceService = cSelect.ReferenceService,
            CreateFormService = createForm,
            ExtraValues = extra
          });
        }
        else if (!resolvedId.HasValue && FormCompositeDraftBuilder.TryExtractExplicitPositiveReferenceId(rawCell, out var badCellId))
        {
          throw FormCompositeDraftBuilder.Refuse(localizer["ReferenceNotFound", $"#{badCellId}", cSelect.ReferenceService!]);
        }
      }
    }

    if (!handledBySymRef && resolvedId == null && rowPending.All(p => !string.Equals(p.FieldPath, cel.Path, StringComparison.OrdinalIgnoreCase)) &&
        parentValues.TryGetValue(cel.Path, out var parentVal) &&
        FormReferenceLookupResolver.TryConvertToLong(parentVal, out var parentId) &&
        parentId > 0)
    {
      resolvedId = parentId;
    }

    rowValues[cel.Path] = resolvedId;
    if (resolvedId is > 0)
    {
      await FormReferenceLookupResolver.EnsureReferenceIdLoadedAsync(cSelect.ReferenceService!, resolvedId.Value, dictionaries, referrerType, db, app, ct);
      if (dictionaries.TryGetValue(cSelect.ReferenceService!, out var dictRows) &&
          dictRows.FirstOrDefault(r => FormReferenceLookupResolver.TryGetRowId(r, out var rid) && rid == resolvedId.Value) is { } matchedRow &&
          FormReferenceLookupResolver.GetRowDisplayName(matchedRow, cSelect.ReferenceLabelPaths) is { Length: > 0 } label)
      {
        rowLabels[cel.Path] = label;
      }
    }
    else if (parentUnresolved.FirstOrDefault(p => p.ChildKey == null && string.Equals(p.FieldPath, cel.Path, StringComparison.OrdinalIgnoreCase)) is { RawName.Length: > 0 } pRef)
    {
      rowLabels[cel.Path] = pRef.RawName;
    }
  }

  private static List<((ChildGridBuilder Builder, Type ChildGridType, string ChildGridService, Type ChildFormType, string ChildFormService, string ChildKey, string Title, IReadOnlyList<FormElementDto> EditableElements) Desc, string Mode, JsonElement Rows)> ExtractRequestedChildInputs(
    JsonElement args,
    JsonElement valuesSource,
    IReadOnlyList<ChildGridBuilder> allChildren,
    IReadOnlyList<(ChildGridBuilder Builder, Type ChildGridType, string ChildGridService, Type ChildFormType, string ChildFormService, string ChildKey, string Title, IReadOnlyList<FormElementDto> EditableElements)> editableChildren,
    Localizer localizer)
  {
    var result = new List<((ChildGridBuilder, Type, string, Type, string, string, string, IReadOnlyList<FormElementDto>), string, JsonElement)>();

    if (FormDraftEngine.TryGetJsonPropertyIgnoreCase(args, "children", out var childrenEl) ||
        !valuesSource.Equals(args) && FormDraftEngine.TryGetJsonPropertyIgnoreCase(valuesSource, "children", out childrenEl))
    {
      if (childrenEl.ValueKind == JsonValueKind.Array)
      {
        foreach (var groupEl in childrenEl.EnumerateArray())
        {
          if (groupEl.ValueKind != JsonValueKind.Object)
          {
            continue;
          }

          var rawKey = FormReferenceLookupResolver.TryGetString(groupEl, "childKey")
            ?? FormReferenceLookupResolver.TryGetString(groupEl, "key")
            ?? FormReferenceLookupResolver.TryGetString(groupEl, "childService")
            ?? FormReferenceLookupResolver.TryGetString(groupEl, "service");
          var desc = MatchChildDescriptor(rawKey, allChildren, editableChildren, localizer);
          var mode = NormalizeChildMode(FormReferenceLookupResolver.TryGetString(groupEl, "mode"));
          if (FormDraftEngine.TryGetJsonPropertyIgnoreCase(groupEl, "rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
          {
            result.Add((desc, mode, rowsEl));
          }
        }
      }
      else if (childrenEl.ValueKind == JsonValueKind.Object)
      {
        foreach (var prop in childrenEl.EnumerateObject())
        {
          var desc = MatchChildDescriptor(prop.Name, allChildren, editableChildren, localizer);
          if (prop.Value.ValueKind == JsonValueKind.Array)
          {
            result.Add((desc, "replace", prop.Value));
          }
          else if (prop.Value.ValueKind == JsonValueKind.Object &&
                   FormDraftEngine.TryGetJsonPropertyIgnoreCase(prop.Value, "rows", out var rowsEl) &&
                   rowsEl.ValueKind == JsonValueKind.Array)
          {
            result.Add((desc, NormalizeChildMode(FormReferenceLookupResolver.TryGetString(prop.Value, "mode")), rowsEl));
          }
        }
      }
    }

    if (result.Count == 0)
    {
      foreach (var childDesc in editableChildren)
      {
        if (TryExtractChildProp(args, valuesSource, childDesc.ChildKey, out var childMode, out var childRowsEl))
        {
          result.Add((childDesc, childMode, childRowsEl));
        }
      }
    }

    if (result.Count == 0 && TryExtractChildProp(args, valuesSource, "lines", out var linesMode, out var linesEl))
    {
      if (editableChildren.Count == 0)
      {
        if (allChildren.Any(c => !c.IsFiles))
        {
          throw FormCompositeDraftBuilder.Refuse(localizer["ChildGridReadOnly"]);
        }
      }
      else
      {
        result.Add((editableChildren[0], linesMode, linesEl));
      }
    }

    return result;
  }

  private static bool TryExtractChildProp(
    JsonElement args,
    JsonElement valuesSource,
    string propName,
    out string mode,
    out JsonElement rowsEl)
  {
    mode = "replace";
    rowsEl = default;
    if (!(FormDraftEngine.TryGetJsonPropertyIgnoreCase(args, propName, out var propEl) ||
          !valuesSource.Equals(args) && FormDraftEngine.TryGetJsonPropertyIgnoreCase(valuesSource, propName, out propEl)))
    {
      return false;
    }

    if (propEl.ValueKind == JsonValueKind.Array)
    {
      var rawMode = FormReferenceLookupResolver.TryGetString(args, $"{propName}Mode")
        ?? FormReferenceLookupResolver.TryGetString(valuesSource, $"{propName}Mode");
      mode = NormalizeChildMode(rawMode);
      rowsEl = propEl;
      return true;
    }

    if (propEl.ValueKind == JsonValueKind.Object &&
        FormDraftEngine.TryGetJsonPropertyIgnoreCase(propEl, "rows", out var nestedRows) &&
        nestedRows.ValueKind == JsonValueKind.Array)
    {
      mode = NormalizeChildMode(FormReferenceLookupResolver.TryGetString(propEl, "mode"));
      rowsEl = nestedRows;
      return true;
    }

    return false;
  }

  private static (ChildGridBuilder Builder, Type ChildGridType, string ChildGridService, Type ChildFormType, string ChildFormService, string ChildKey, string Title, IReadOnlyList<FormElementDto> EditableElements) MatchChildDescriptor(
    string? rawKey,
    IReadOnlyList<ChildGridBuilder> allChildren,
    IReadOnlyList<(ChildGridBuilder Builder, Type ChildGridType, string ChildGridService, Type ChildFormType, string ChildFormService, string ChildKey, string Title, IReadOnlyList<FormElementDto> EditableElements)> editableChildren,
    Localizer localizer)
  {
    if (string.IsNullOrWhiteSpace(rawKey))
    {
      return editableChildren.Count == 1
        ? editableChildren[0]
        : throw FormCompositeDraftBuilder.Refuse(localizer["ChildKeyRequired"]);
    }

    var key = rawKey.Trim();
    var matched = editableChildren.FirstOrDefault(c =>
      string.Equals(c.ChildKey, key, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(c.ChildGridService, key, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(c.ChildFormService, key, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(c.Title, key, StringComparison.OrdinalIgnoreCase));
    if (matched.Builder != null)
    {
      return matched;
    }

    var singular = key.EndsWith("s", StringComparison.OrdinalIgnoreCase) && key.Length > 1 ? key[..^1] : key;
    var plural = $"{singular}s";
    matched = editableChildren.FirstOrDefault(c =>
      string.Equals(c.ChildKey, singular, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(c.ChildKey, plural, StringComparison.OrdinalIgnoreCase) ||
      c.ChildGridService.EndsWith($"-{singular}", StringComparison.OrdinalIgnoreCase) ||
      c.ChildGridService.EndsWith($"-{plural}", StringComparison.OrdinalIgnoreCase));
    if (matched.Builder != null)
    {
      return matched;
    }

    if (editableChildren.Count == 1 &&
        (string.Equals(key, "lines", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(key, "items", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(key, "rows", StringComparison.OrdinalIgnoreCase)))
    {
      return editableChildren[0];
    }

    var blocked = allChildren.FirstOrDefault(c =>
      !c.IsFiles &&
      (string.Equals(c.DeclaredKey, key, StringComparison.OrdinalIgnoreCase) ||
       string.Equals(c.ServicePath, key, StringComparison.OrdinalIgnoreCase)));
    if (blocked != null)
    {
      throw FormCompositeDraftBuilder.Refuse(localizer["ChildGridNamedReadOnly", key]);
    }

    throw FormCompositeDraftBuilder.Refuse(localizer["ChildGridUnknown", key]);
  }

  private static string NormalizeChildMode(string? rawMode) =>
    string.Equals(rawMode?.Trim(), "append", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(rawMode?.Trim(), "add", StringComparison.OrdinalIgnoreCase)
      ? "append"
      : "replace";

  private static long? TryGetRecordId(JsonElement element) =>
    (FormReferenceLookupResolver.TryGetLong(element, "id") ?? FormReferenceLookupResolver.TryGetLong(element, "recordId")) is > 0 and var id
      ? id
      : null;
}
