using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalChildExecutor
{
  public const int MaxReplaceableChildRows = 5000;
  private const int PageBatchSize = 500;

  public static async Task ExecuteChildGroupsAsync(
    IFormService parentForm,
    long recordId,
    bool isEdit,
    Dictionary<string, object?> parentValues,
    IReadOnlyList<ChildGridBuilder> declaredChildren,
    IReadOnlyList<StagedChildGroupPayload> stagedChildren,
    IReadOnlyList<AgentModuleScope> scopes,
    AgentModuleScope parentScope,
    List<MessageDto> collectedWarnings,
    Localizer L,
    CancellationToken mutationCt)
  {
    foreach (var childGroup in stagedChildren)
    {
      if (!(isEdit ? childGroup.HasUpdate : childGroup.Rows.Count > 0) ||
          string.IsNullOrWhiteSpace(childGroup.ChildFormService))
      {
        continue;
      }

      var childFormType = ModuleCatalog.Instance.FindServiceType(childGroup.ChildFormService)
        ?? throw AgentRefusal.Create(L["MutationRefused"]);
      var childScope = AgentBatchTransactionRunner.SelectScopeForService(scopes, childFormType, parentScope);

      if (ModuleCatalog.Instance.CreateServiceInstance(childFormType, childScope.Db, childScope.App) is not IFormService metaChildForm ||
          metaChildForm.Feature.IsReadOnly ||
          !AgentFeatureRegistry.IsChildAgentWriteAllowed(metaChildForm.Feature, parentForm.Feature) ||
          !AgentFeatureRegistry.CanEditFeature(metaChildForm.Feature, childScope.App))
      {
        throw AgentRefusal.Create(L["MutationRefused"]);
      }

      metaChildForm.SetRequest(new FormRequest { ParentService = parentForm.Service, ParentId = recordId });
      var (childElements, _) = await metaChildForm.GetElementsMetadataAsync(mutationCt);
      var hasSortOrderField = childElements.Any(e => string.Equals(e.Path, "sortOrder", StringComparison.OrdinalIgnoreCase));
      var childPrimaryTextKey = ResolvePrimaryTextFieldPath(childElements);

      var isAppend = string.Equals(childGroup.Mode, AgentProposalChildMode.Append, StringComparison.OrdinalIgnoreCase);
      var childGrid = ResolveValidatedChildGrid(childGroup, declaredChildren, parentForm.Feature, scopes, childScope, L);

      var (existingLineIds, maxSortOrder) = isEdit && childGrid != null
        ? await LoadAllExistingChildIdsAsync(childGrid, parentForm.Service, recordId, isAppend, childGroup.ChildKey, mutationCt)
        : ([], 0);

      var sortOrder = isEdit && isAppend ? Math.Max(existingLineIds.Count, maxSortOrder) : 0;

      if (isEdit && !isAppend && childGrid != null)
      {
        var updatedLineIds = CollectUpdatedLineIds(childGroup.Rows, existingLineIds);
        var idsToDelete = existingLineIds.Where(id => !updatedLineIds.Contains(id)).ToList();
        if (idsToDelete.Count > 0)
        {
          await DeleteObsoleteChildRowsAsync(childGrid, parentForm.Service, recordId, idsToDelete, L, mutationCt);
        }
      }

      foreach (var lineDict in childGroup.Rows)
      {
        sortOrder++;
        if (hasSortOrderField && (!lineDict.ContainsKey("sortOrder") || isAppend))
        {
          lineDict["sortOrder"] = sortOrder;
        }

        PropagateUnsetParentIds(parentValues, lineDict);

        long? existingLineId = isEdit &&
                               lineDict.TryGetValue("id", out var lineIdObj) &&
                               TryConvertToLong(lineIdObj, out var parsedLineId) &&
                               parsedLineId > 0
          ? parsedLineId
          : null;

        if (ModuleCatalog.Instance.CreateServiceInstance(childFormType, childScope.Db, childScope.App) is not IFormService childFormInst)
        {
          throw AgentRefusal.Create(L["MutationRefused"]);
        }

        var lineJson = JsonSerializer.SerializeToElement(lineDict, WebJson);
        try
        {
          var savedLine = await childFormInst.SaveAsync(
            lineJson,
            new FormRequest
            {
              Id = existingLineId,
              ParentService = parentForm.Service,
              ParentId = recordId,
              Value = lineJson
            },
            mutationCt);

          if (!TryExtractSavedId(savedLine, out _))
          {
            var lineName = FormatChildLineLabel(lineDict, childPrimaryTextKey, sortOrder);
            var lineDto = savedLine as FormDataDto;
            var lineErr = lineDto?.Messages?.FirstOrDefault()?.Detail
              ?? lineDto?.Warnings?.FirstOrDefault()?.Detail
              ?? L["MutationRefused"];
            throw AgentRefusal.Create($"{lineName}: {lineErr}");
          }

          if (childFormInst.GetLastSaveWarnings() is { Count: > 0 } childWarnings)
          {
            collectedWarnings.AddRange(childWarnings);
          }
        }
        catch (FormRefusedException refusedEx)
        {
          var lineName = FormatChildLineLabel(lineDict, childPrimaryTextKey, sortOrder);
          throw AgentRefusal.Create(FormatRefusedException(refusedEx, childElements, lineName), refusedEx);
        }
      }
    }
  }

  private static IGridService? ResolveValidatedChildGrid(
    StagedChildGroupPayload childGroup,
    IReadOnlyList<ChildGridBuilder> declaredChildren,
    Feature parentFeature,
    IReadOnlyList<AgentModuleScope> scopes,
    AgentModuleScope fallbackScope,
    Localizer L)
  {
    var gridType = (!string.IsNullOrWhiteSpace(childGroup.ChildGridService)
      ? ModuleCatalog.Instance.FindServiceType(childGroup.ChildGridService)
      : null) ?? (declaredChildren.FirstOrDefault(c =>
        !c.IsFiles && !c.IsReadOnly && c.ServiceType != null &&
        (string.Equals(c.DeclaredKey, childGroup.ChildKey, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(c.ServicePath, childGroup.ChildGridService, StringComparison.OrdinalIgnoreCase)))?.ServiceType);
    if (gridType == null || !typeof(IGridService).IsAssignableFrom(gridType))
    {
      return null;
    }

    var gridScope = AgentBatchTransactionRunner.SelectScopeForService(scopes, gridType, fallbackScope);
    if (ModuleCatalog.Instance.CreateServiceInstance(gridType, gridScope.Db, gridScope.App) is not IGridService cg)
    {
      return null;
    }

    if (cg.Feature.IsReadOnly ||
        !AgentFeatureRegistry.IsChildAgentWriteAllowed(cg.Feature, parentFeature) ||
        !AgentFeatureRegistry.CanViewFeature(cg.Feature, gridScope.App) ||
        !AgentFeatureRegistry.CanEditFeature(cg.Feature, gridScope.App))
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    return cg;
  }

  private static async Task<(List<long> Ids, int MaxSortOrder)> LoadAllExistingChildIdsAsync(
    IGridService childGrid,
    string parentService,
    long recordId,
    bool isAppend,
    string childKey,
    CancellationToken ct)
  {
    var existingLineIds = new List<long>();
    var seen = new HashSet<long>();
    var maxSortOrder = 0;
    var page = 0;

    while (true)
    {
      var existingPage = await childGrid.GetDataAsync(
        new GridRequest
        {
          ParentService = parentService,
          ParentId = recordId,
          Page = page,
          PageSize = PageBatchSize,
          IsDataOnly = true
        },
        ct);

      if (!isAppend && existingPage.Total > MaxReplaceableChildRows)
      {
        throw AgentRefusal.Create(
          Localizer.Instance["AgentChildReplaceLimitExceeded", MaxReplaceableChildRows, childKey]);
      }

      foreach (var rowObj in existingPage.Rows)
      {
        if (rowObj.Id is > 0 and var lid && seen.Add(lid))
        {
          existingLineIds.Add(lid);
        }

        if (rowObj.Cells.TryGetValue("sortOrder", out var soObj) &&
            TryConvertToLong(soObj, out var soVal) &&
            soVal > maxSortOrder)
        {
          maxSortOrder = (int)soVal;
        }
      }

      if (!isAppend && existingLineIds.Count > MaxReplaceableChildRows)
      {
        throw AgentRefusal.Create(
          Localizer.Instance["AgentChildReplaceLimitExceeded", MaxReplaceableChildRows, childKey]);
      }

      if (existingPage.Rows.Count < PageBatchSize ||
          existingPage.Total is { } total && existingLineIds.Count >= total)
      {
        break;
      }

      page++;
    }

    return (existingLineIds, maxSortOrder);
  }

  private static HashSet<long> CollectUpdatedLineIds(
    List<Dictionary<string, object?>> rows,
    List<long> existingLineIds)
  {
    var existingSet = existingLineIds.ToHashSet();
    var updated = new HashSet<long>();
    foreach (var lineDict in rows)
    {
      if (lineDict.TryGetValue("id", out var lidObj) &&
          TryConvertToLong(lidObj, out var parsedLid) &&
          parsedLid > 0 &&
          existingSet.Contains(parsedLid))
      {
        updated.Add(parsedLid);
      }
    }

    return updated;
  }

  private static async Task DeleteObsoleteChildRowsAsync(
    IGridService childGrid,
    string parentService,
    long recordId,
    List<long> idsToDelete,
    Localizer L,
    CancellationToken ct)
  {
    if (!childGrid.CanDelete())
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    if (childGrid.AllowsMultiDelete)
    {
      for (var offset = 0; offset < idsToDelete.Count; offset += PageBatchSize)
      {
        var chunk = idsToDelete.Skip(offset).Take(PageBatchSize).ToList();
        var delRes = await childGrid.DeleteAsync(
          new GridRequest { ParentService = parentService, ParentId = recordId, SelectedIds = chunk },
          ct);
        if (delRes.Outcome != MutationOutcome.Committed)
        {
          var msg = delRes.Messages?.FirstOrDefault()?.Detail ?? delRes.Warnings?.FirstOrDefault()?.Detail ?? delRes.Title ?? L["MutationRefused"];
          throw AgentRefusal.Create(msg);
        }
      }

      return;
    }

    foreach (var lineId in idsToDelete)
    {
      var delRes = await childGrid.DeleteAsync(
        new GridRequest { ParentService = parentService, ParentId = recordId, SelectedIds = [lineId] },
        ct);
      if (delRes.Outcome != MutationOutcome.Committed)
      {
        var msg = delRes.Messages?.FirstOrDefault()?.Detail ?? delRes.Warnings?.FirstOrDefault()?.Detail ?? delRes.Title ?? L["MutationRefused"];
        throw AgentRefusal.Create(msg);
      }
    }
  }

  private static string FormatChildLineLabel(Dictionary<string, object?> lineDict, string primaryTextKey, int sortOrder) =>
    lineDict.TryGetValue(primaryTextKey, out var nObj) && nObj?.ToString() is { } text && !string.IsNullOrWhiteSpace(text)
      ? text
      : $"#{sortOrder}";

  private static void PropagateUnsetParentIds(Dictionary<string, object?> parentValues, Dictionary<string, object?> lineDict)
  {
    foreach (var (k, v) in parentValues)
    {
      if (TryConvertToLong(v, out var parentRefId) && parentRefId > 0 &&
          lineDict.TryGetValue(k, out var existingLineVal) &&
          (existingLineVal == null || existingLineVal is JsonElement { ValueKind: JsonValueKind.Null }))
      {
        lineDict[k] = parentRefId;
      }
    }
  }
}
