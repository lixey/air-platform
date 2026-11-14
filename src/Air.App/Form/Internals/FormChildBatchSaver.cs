using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormChildBatchSaver
{
  public const int MaxReplaceableChildRows = 5000;
  private const int PageBatchSize = 500;

  private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
  {
    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
  };

  public static async Task<FormChildBatchResultDto> ApplyAsync(
    IFormService parentForm,
    Type parentFormType,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    long parentId,
    bool isParentEdit,
    IReadOnlyDictionary<string, object?>? parentValues,
    IReadOnlyList<ChildGridBuilder> declaredChildren,
    IReadOnlyList<FormChildBatchGroupDto> groups,
    Func<Feature, Feature, bool>? childPermissionFilter,
    CancellationToken ct)
  {
    var warnings = new List<MessageDto>();
    var anyChildMutated = false;

    var currentTarget = (ParentService: parentForm.Service, ParentId: parentId);
    var prevDeferred = BoundParentRecalculator.SetDeferredTarget(currentTarget);
    try
    {
      foreach (var group in groups)
      {
        if (!(isParentEdit ? group.HasUpdate : group.Rows.Count > 0) ||
            string.IsNullOrWhiteSpace(group.ChildFormService))
        {
          continue;
        }

        var childFormType = ModuleCatalog.Instance.FindServiceType(group.ChildFormService)
          ?? throw Refuse(localizer["MutationRefused"]);
        if (ModuleCatalog.Instance.CreateServiceInstance(childFormType, db, app) is not IFormService metaChildForm ||
            metaChildForm.Feature.IsReadOnly ||
            childPermissionFilter != null && !childPermissionFilter(metaChildForm.Feature, parentForm.Feature) ||
            !CanWriteChildFeature(metaChildForm.Feature, db, app))
        {
          throw Refuse(localizer["MutationRefused"]);
        }

        metaChildForm.SetRequest(new FormRequest { ParentService = parentForm.Service, ParentId = parentId });
        var (childElements, _) = await metaChildForm.GetElementsMetadataAsync(ct);
        var hasSortOrderField = childElements.Any(e => string.Equals(e.Path, "sortOrder", StringComparison.OrdinalIgnoreCase));
        var primaryTextKey = ResolvePrimaryTextKey(childElements);
        var isAppend = string.Equals(group.Mode, "append", StringComparison.OrdinalIgnoreCase);
        var childGrid = ResolveValidatedChildGrid(group, declaredChildren, parentForm.Feature, db, app, childPermissionFilter, localizer);

        var (existingIds, maxSortOrder) = isParentEdit && childGrid != null
          ? await LoadAllExistingChildIdsAsync(childGrid, parentForm.Service, parentId, isAppend, group.ChildKey, localizer, ct)
          : ([], 0);

        var sortOrder = isParentEdit && isAppend ? Math.Max(existingIds.Count, maxSortOrder) : 0;

        if (isParentEdit && !isAppend && childGrid != null)
        {
          var existingSet = existingIds.ToHashSet();
          var retainedIds = new HashSet<long>();
          foreach (var row in group.Rows)
          {
            if (row.TryGetValue("id", out var lidObj) &&
                FormReferenceLookupResolver.TryConvertToLong(lidObj, out var parsedLid) &&
                parsedLid > 0 &&
                existingSet.Contains(parsedLid))
            {
              retainedIds.Add(parsedLid);
            }
          }

          var idsToDelete = existingIds.Where(id => !retainedIds.Contains(id)).ToList();
          if (idsToDelete.Count > 0)
          {
            await DeleteObsoleteChildRowsAsync(childGrid, parentForm.Service, parentId, idsToDelete, localizer, ct);
            anyChildMutated = true;
          }
        }

        foreach (var lineDict in group.Rows)
        {
          sortOrder++;
          if (hasSortOrderField && (!lineDict.ContainsKey("sortOrder") || isAppend))
          {
            lineDict["sortOrder"] = sortOrder;
          }

          if (parentValues != null)
          {
            PropagateUnsetParentIds(parentValues, lineDict);
          }

          long? existingLineId = isParentEdit &&
                                 lineDict.TryGetValue("id", out var lineIdObj) &&
                                 FormReferenceLookupResolver.TryConvertToLong(lineIdObj, out var parsedLineId) &&
                                 parsedLineId > 0
            ? parsedLineId
            : null;
          int? existingLineVer = existingLineId.HasValue &&
                                 lineDict.TryGetValue("ver", out var verObj) &&
                                 FormReferenceLookupResolver.TryConvertToLong(verObj, out var parsedVer)
            ? (int)parsedVer
            : null;

          if (ModuleCatalog.Instance.CreateServiceInstance(childFormType, db, app) is not IFormService childFormInst)
          {
            throw Refuse(localizer["MutationRefused"]);
          }

          var lineJson = JsonSerializer.SerializeToElement(lineDict, WebJson);
          try
          {
            var saved = await childFormInst.SaveRecordAsync(
              new FormRequest
              {
                Id = existingLineId,
                Ver = existingLineVer,
                ParentService = parentForm.Service,
                ParentId = parentId,
                Value = lineJson
              },
              ct);

            if (saved.Id <= 0)
            {
              var lineLabel = FormatLineLabel(lineDict, primaryTextKey, sortOrder);
              var err = saved.Messages.FirstOrDefault()?.Detail ?? localizer["MutationRefused"];
              throw Refuse($"{lineLabel}: {err}");
            }

            anyChildMutated = true;
            if (saved.Warnings.Count > 0)
            {
              warnings.AddRange(saved.Warnings);
            }
          }
          catch (FormRefusedException refusedEx)
          {
            var lineLabel = FormatLineLabel(lineDict, primaryTextKey, sortOrder);
            throw WrapLineRefusal(refusedEx, childElements, lineLabel, localizer);
          }
        }
      }
    }
    finally
    {
      BoundParentRecalculator.SetDeferredTarget(prevDeferred);
    }

    List<AffectedDto>? affected = null;
    if (anyChildMutated && prevDeferred != currentTarget)
    {
      affected = await BoundParentRecalculator.RecalculateBoundParentsAsync(
        db,
        app,
        parentForm.Service,
        parentForm.Service,
        parentFormType,
        [parentId],
        ct,
        requestParentId: parentId);
    }

    return new FormChildBatchResultDto
    {
      Warnings = warnings,
      Affected = affected
    };
  }

  private static Grid.IGridService? ResolveValidatedChildGrid(
    FormChildBatchGroupDto group,
    IReadOnlyList<ChildGridBuilder> declaredChildren,
    Feature parentFeature,
    PlatformDbContext db,
    PlatformAppService? app,
    Func<Feature, Feature, bool>? childPermissionFilter,
    Localizer localizer)
  {
    var gridType = (!string.IsNullOrWhiteSpace(group.ChildGridService)
      ? ModuleCatalog.Instance.FindServiceType(group.ChildGridService)
      : null) ?? declaredChildren.FirstOrDefault(c =>
        !c.IsFiles && !c.IsReadOnly && c.ServiceType != null &&
        (string.Equals(c.DeclaredKey, group.ChildKey, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(c.ServicePath, group.ChildGridService, StringComparison.OrdinalIgnoreCase)))?.ServiceType;
    if (gridType == null || !typeof(Grid.IGridService).IsAssignableFrom(gridType))
    {
      return null;
    }

    if (ModuleCatalog.Instance.CreateServiceInstance(gridType, db, app) is not Grid.IGridService cg)
    {
      return null;
    }

    if (cg.Feature.IsReadOnly ||
        childPermissionFilter != null && !childPermissionFilter(cg.Feature, parentFeature) ||
        !ServiceEndpointsExtensions.CanView(cg.Feature, app?.User) ||
        !CanWriteChildFeature(cg.Feature, db, app))
    {
      throw Refuse(localizer["MutationRefused"]);
    }

    return cg;
  }

  private static async Task<(List<long> Ids, int MaxSortOrder)> LoadAllExistingChildIdsAsync(
    Grid.IGridService childGrid,
    string parentService,
    long parentId,
    bool isAppend,
    string childKey,
    Localizer localizer,
    CancellationToken ct)
  {
    var existingIds = new List<long>();
    var seen = new HashSet<long>();
    var maxSortOrder = 0;
    var page = 0;

    while (true)
    {
      var existingPage = await childGrid.GetDataAsync(
        new GridRequest
        {
          ParentService = parentService,
          ParentId = parentId,
          Page = page,
          PageSize = PageBatchSize,
          IsDataOnly = true
        },
        ct);

      if (!isAppend && existingPage.Total > MaxReplaceableChildRows)
      {
        throw Refuse(localizer["AgentChildReplaceLimitExceeded", MaxReplaceableChildRows, childKey]);
      }

      foreach (var rowObj in existingPage.Rows)
      {
        if (rowObj.Id is > 0 and var lid && seen.Add(lid))
        {
          existingIds.Add(lid);
        }

        if (rowObj.Cells.TryGetValue("sortOrder", out var soObj) &&
            FormReferenceLookupResolver.TryConvertToLong(soObj, out var soVal) &&
            soVal > maxSortOrder)
        {
          maxSortOrder = (int)soVal;
        }
      }

      if (!isAppend && existingIds.Count > MaxReplaceableChildRows)
      {
        throw Refuse(localizer["AgentChildReplaceLimitExceeded", MaxReplaceableChildRows, childKey]);
      }

      if (existingPage.Rows.Count < PageBatchSize ||
          existingPage.Total is { } total && existingIds.Count >= total)
      {
        break;
      }

      page++;
    }

    return (existingIds, maxSortOrder);
  }

  private static async Task DeleteObsoleteChildRowsAsync(
    Grid.IGridService childGrid,
    string parentService,
    long parentId,
    List<long> idsToDelete,
    Localizer localizer,
    CancellationToken ct)
  {
    if (!childGrid.CanDelete())
    {
      throw Refuse(localizer["MutationRefused"]);
    }

    if (childGrid.AllowsMultiDelete)
    {
      for (var offset = 0; offset < idsToDelete.Count; offset += PageBatchSize)
      {
        var chunk = idsToDelete.Skip(offset).Take(PageBatchSize).ToList();
        var delRes = await childGrid.DeleteAsync(
          new GridRequest { ParentService = parentService, ParentId = parentId, SelectedIds = chunk },
          ct);
        if (delRes.Outcome != MutationOutcome.Committed)
        {
          var msg = delRes.Messages?.FirstOrDefault()?.Detail ?? delRes.Title ?? localizer["MutationRefused"];
          throw Refuse(msg);
        }
      }

      return;
    }

    foreach (var lineId in idsToDelete)
    {
      var delRes = await childGrid.DeleteAsync(
        new GridRequest { ParentService = parentService, ParentId = parentId, SelectedIds = [lineId] },
        ct);
      if (delRes.Outcome != MutationOutcome.Committed)
      {
        var msg = delRes.Messages?.FirstOrDefault()?.Detail ?? delRes.Title ?? localizer["MutationRefused"];
        throw Refuse(msg);
      }
    }
  }

  private static void PropagateUnsetParentIds(IReadOnlyDictionary<string, object?> parentValues, Dictionary<string, object?> lineDict)
  {
    foreach (var (k, v) in parentValues)
    {
      if (FormReferenceLookupResolver.TryConvertToLong(v, out var parentRefId) && parentRefId > 0 &&
          lineDict.TryGetValue(k, out var existingLineVal) &&
          (existingLineVal == null || existingLineVal is JsonElement { ValueKind: JsonValueKind.Null }))
      {
        lineDict[k] = parentRefId;
      }
    }
  }

  private static bool CanWriteChildFeature(Feature feature, PlatformDbContext db, PlatformAppService? app) =>
    db.TenantContext.IsSystemScope ||
    ServiceEndpointsExtensions.CanEdit(feature, app?.User);

  private static string ResolvePrimaryTextKey(IReadOnlyList<FormElementDto> elements) =>
    elements.FirstOrDefault(e => FormReferenceLookupResolver.IsEditableElement(e) && e.Type == UiFieldType.Text && e.Model is not FormSelectModel)?.Path
    ?? "name";

  private static string FormatLineLabel(Dictionary<string, object?> lineDict, string primaryTextKey, int sortOrder) =>
    lineDict.TryGetValue(primaryTextKey, out var nObj) && nObj?.ToString() is { } text && !string.IsNullOrWhiteSpace(text)
      ? text
      : $"#{sortOrder}";

  private static FormRefusedException Refuse(string message) =>
    new(MutationOutcome.Refused, message, [new MessageDto { Detail = message, Type = MessageType.Error }]);

  private static FormRefusedException WrapLineRefusal(
    FormRefusedException ex,
    IReadOnlyList<FormElementDto> elements,
    string lineLabel,
    Localizer localizer)
  {
    if (ex.Messages.Count == 0)
    {
      return Refuse($"{lineLabel}: {ex.Message}");
    }

    var formatted = ex.Messages
      .Select(m =>
      {
        var detail = m.Detail ?? m.Title ?? ex.Message;
        if (!string.IsNullOrEmpty(m.Path))
        {
          var label = elements.FirstOrDefault(e => string.Equals(e.Path, m.Path, StringComparison.OrdinalIgnoreCase))?.Label ?? m.Path;
          detail = $"{label}: {detail}";
        }

        return new MessageDto { Code = m.Code, Path = m.Path, RecordId = m.RecordId, Detail = $"{lineLabel} — {detail}", Type = m.Type };
      })
      .ToList();
    return new FormRefusedException(ex.Outcome, formatted[0].Detail ?? localizer["FormInvalid"], formatted);
  }
}
