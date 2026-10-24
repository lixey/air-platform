namespace Platform.Agent;

public static partial class AgentProposalBuilder
{
  private static async Task<(string? Warning, long? RecordId, string? FormService)> DetectDuplicateRecordAsync(
    IFormService formInst,
    GridCreateDescriptor targetDesc,
    IReadOnlyList<GridCreateDescriptor> descriptors,
    IReadOnlyList<FormElementDto> editableElements,
    Type? fileChildServiceType,
    Dictionary<string, object?> formValues,
    IReadOnlyList<string> attachmentIds,
    string? currencyLabel,
    AgentStore store,
    AgentOwnerScope owner,
    PlatformDbContext db,
    PlatformAppService app,
    Localizer L,
    CancellationToken ct)
  {
    try
    {
      long? duplicateId = null;
      var isExactFileMatch = false;
      Dictionary<string, object?>? matchedRow = null;

      if (attachmentIds.Count > 0 &&
          fileChildServiceType != null &&
          ModuleCatalog.Instance.CreateServiceInstance(fileChildServiceType, db, app) is IFormFilesSource fileSource)
      {
        foreach (var attId in attachmentIds)
        {
          var att = store.GetAttachment(attId, owner);
          if (att == null)
          {
            continue;
          }

          var sha256 = att.Sha256;
          if (string.IsNullOrWhiteSpace(sha256) && File.Exists(att.LocalPath))
          {
            var bytes = await File.ReadAllBytesAsync(att.LocalPath, ct);
            sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            att.Sha256 = sha256;
          }

          if (!string.IsNullOrWhiteSpace(sha256))
          {
            var existingParentId = await fileSource.FindExistingParentIdByHashAsync(sha256, ct);
            if (existingParentId is > 0)
            {
              duplicateId = existingParentId.Value;
              isExactFileMatch = true;
              break;
            }
          }
        }
      }

      IGridService? ownerGrid = null;
      if (formInst.Feature.ServiceType != null &&
          typeof(IGridService).IsAssignableFrom(formInst.Feature.ServiceType) &&
          ModuleCatalog.Instance.CreateServiceInstance(formInst.Feature.ServiceType, db, app) is IGridService g &&
          AgentFeatureRegistry.CanViewFeature(g.Feature, app))
      {
        ownerGrid = g;
      }

      var datePath = editableElements.FirstOrDefault(e => e.Type == UiFieldType.Date)?.Path;
      var moneyPath = editableElements.FirstOrDefault(e => e.Model is FormMoneyModel)?.Path;
      var primaryRefEl = editableElements.FirstOrDefault(e =>
        e.Model is FormSelectModel { IsMultiple: false } &&
        e.CreateFormServiceType != null &&
        e.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase));
      var primaryRefNav = primaryRefEl?.Path[..^2];
      var refNavPaths = editableElements
        .Where(e => e.Model is FormSelectModel { IsMultiple: false } && e.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        .Select(e => e.Path[..^2])
        .ToList();

      if (duplicateId == null &&
          ownerGrid != null &&
          !string.IsNullOrEmpty(datePath) &&
          !string.IsNullOrEmpty(moneyPath) &&
          formValues.TryGetValue(datePath, out var dateObj) &&
          dateObj?.ToString()?.Trim() is { Length: >= 10 } rawDate &&
          formValues.TryGetValue(moneyPath, out var amtObj) &&
          TryConvertToDecimal(amtObj, out var proposedAmt) &&
          proposedAmt > 0m)
      {
        var dateStr = rawDate[..10];
        var proposedRefId = primaryRefEl != null &&
                            formValues.TryGetValue(primaryRefEl.Path, out var cpObj) &&
                            TryConvertToLong(cpObj, out var cpId) &&
                            cpId > 0
          ? cpId
          : (long?)null;

        var dayPage = await ownerGrid.GetDataAsync(new ListRequest
        {
          Page = 0,
          PageSize = 50,
          Filter = FilterNode.Rule(datePath, FilterOperator.Equals, dateStr)
        }, ct);

        foreach (var rawRow in dayPage.Rows)
        {
          if (CleanGridRow(rawRow) is not { } row || !TryGetRowId(row, out var rowId) || rowId <= 0)
          {
            continue;
          }

          if (row.TryGetValue(datePath, out var rowDateObj) &&
              rowDateObj?.ToString() is { Length: >= 10 } rowDateStr &&
              !string.Equals(rowDateStr[..10], dateStr, StringComparison.Ordinal))
          {
            continue;
          }

          var rAmt = 0m;
          var hasAmt = (row.TryGetValue(moneyPath, out var rAmtObj) || row.TryGetValue("signedAmount", out rAmtObj)) &&
                       TryConvertToDecimal(rAmtObj, out rAmt);
          if (!hasAmt || Math.Abs(Math.Abs(rAmt) - proposedAmt) >= 0.01m)
          {
            continue;
          }

          if (duplicateId == null)
          {
            duplicateId = rowId;
            matchedRow = row;
          }

          if (proposedRefId.HasValue &&
              !string.IsNullOrEmpty(primaryRefNav) &&
              row.TryGetValue(primaryRefNav, out var rowCpObj) &&
              TryConvertToLong(UnwrapReferenceValue(rowCpObj), out var rowCpId) &&
              rowCpId == proposedRefId.Value)
          {
            duplicateId = rowId;
            matchedRow = row;
            break;
          }
        }
      }

      if (duplicateId is not > 0)
      {
        return (null, null, null);
      }

      if (matchedRow == null && ownerGrid != null)
      {
        var readObj = await ownerGrid.ReadRowAsync(duplicateId.Value, ct);
        matchedRow = CleanGridRow(readObj);
      }

      var dupDesc = await AgentFeatureRegistry.ResolveEditFormDescriptorAsync(
        descriptors,
        targetDesc,
        targetDesc.ServicePath,
        duplicateId.Value,
        db,
        app,
        ct);

      var summaryParts = new List<string> { $"#{duplicateId.Value}" };
      if (matchedRow != null)
      {
        var orderedNavs = !string.IsNullOrEmpty(primaryRefNav)
          ? new[] { primaryRefNav }.Concat(refNavPaths.Where(p => !string.Equals(p, primaryRefNav, StringComparison.OrdinalIgnoreCase)))
          : refNavPaths;

        foreach (var navPath in orderedNavs)
        {
          if (matchedRow.TryGetValue(navPath, out var refVal) &&
              refVal is IDictionary<string, object?> refDict &&
              refDict.TryGetValue("name", out var refName) &&
              refName?.ToString() is { Length: > 0 } refStr)
          {
            summaryParts.Add(refStr);
            break;
          }
        }

        if ((matchedRow.TryGetValue("signedAmount", out var saVal) ||
             !string.IsNullOrEmpty(moneyPath) && matchedRow.TryGetValue(moneyPath, out saVal)) &&
            TryConvertToDecimal(saVal, out var saDec) &&
            saDec != 0m)
        {
          var absAmt = Math.Abs(saDec).ToString("N2", CultureInfo.CurrentUICulture);
          summaryParts.Add(!string.IsNullOrEmpty(currencyLabel) ? $"{absAmt} {currencyLabel}" : absAmt);
        }

        if (!string.IsNullOrEmpty(datePath) &&
            matchedRow.TryGetValue(datePath, out var dVal) &&
            dVal?.ToString() is { Length: >= 10 } dStr)
        {
          summaryParts.Add(dStr[..10]);
        }
      }

      var summary = string.Join(" · ", summaryParts);
      var warning = isExactFileMatch
        ? L["AgentDuplicateFileWarning", summary]
        : L["AgentDuplicateReceiptWarning", summary];

      return (warning, duplicateId.Value, dupDesc.ServicePath);
    }
    catch
    {
      return (null, null, null);
    }
  }
}
