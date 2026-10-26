using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static partial class AgentProposalBuilder
{
  public static async Task<AgentProposalDto> StageProposalAsync(
    JsonElement args,
    IServiceProvider services,
    CancellationToken ct,
    IReadOnlyList<string>? fallbackAttachmentIds = null)
  {
    var db = services.GetRequiredService<PlatformDbContext>();
    var app = services.GetRequiredService<PlatformAppService>();
    var store = services.GetRequiredService<AgentStore>();
    var options = services.GetService<AgentOptions>() ?? new AgentOptions();
    var bridge = services.GetService<McpExecutionBridge>();
    var L = Localizer.Instance;
    var owner = AgentStore.ResolveOwner(services);

    var sessionId = TryGetString(args, "sessionId")
      ?? bridge?.Active?.SessionId
      ?? "default";

    var descriptors = AgentFeatureRegistry.DiscoverForms(db, app);
    if (descriptors.Count == 0)
    {
      throw new InvalidOperationException("No agent-enabled forms are available for the current user.");
    }

    var hasNestedValues = args.TryGetProperty("values", out var vProp) && vProp.ValueKind == JsonValueKind.Object;
    var valuesSource = hasNestedValues ? vProp : args;

    var recordId = TryGetRecordId(args) ?? (hasNestedValues ? TryGetRecordId(vProp) : null);
    var isEdit = recordId is > 0;

    var requestedService = TryGetString(args, "service") ?? TryGetString(args, "kind");
    var targetDesc = AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requestedService, db, app) ?? descriptors[0];
    if (isEdit)
    {
      targetDesc = await AgentFeatureRegistry.ResolveEditFormDescriptorAsync(descriptors, targetDesc, requestedService, recordId!.Value, db, app, ct);
    }

    if (ModuleCatalog.Instance.CreateServiceInstance(targetDesc.FormServiceType, db, app) is not IFormService formInst ||
        !AgentFeatureRegistry.CanEditFeature(formInst.Feature, app))
    {
      throw new InvalidOperationException($"Access denied or form service '{targetDesc.ServicePath}' is unavailable.");
    }

    var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
    var editableElements = elements.Where(AgentFeatureRegistry.IsEditableElement).ToList();

    Dictionary<string, object?> originalFormValues;
    Dictionary<string, object?> formValues;
    if (isEdit)
    {
      var existingValues = await AgentContextQueryService.GetExistingFormValuesAsync(formInst, recordId!.Value, editableElements, ct);
      originalFormValues = CloneFormValues(existingValues);
      formValues = CloneFormValues(existingValues);
    }
    else
    {
      var defaultValues = await AgentContextQueryService.GetDefaultFormValuesAsync(formInst, editableElements, ct);
      originalFormValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
      formValues = new Dictionary<string, object?>(defaultValues, StringComparer.OrdinalIgnoreCase);
    }

    var refServices = editableElements
      .Select(e => (e.Model as FormSelectModel)?.ReferenceService)
      .Where(s => !string.IsNullOrEmpty(s))
      .Cast<string>()
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var lineChild = children.FirstOrDefault(c => !c.IsFiles && c.ServiceType != null && typeof(IGridService).IsAssignableFrom(c.ServiceType));
    GridCreateDescriptor? childFormDesc = null;
    IFormService? childFormInst = null;
    List<FormElementDto> childEditableElements = [];
    if (lineChild?.ServiceType != null &&
        ModuleCatalog.Instance.CreateServiceInstance(lineChild.ServiceType, db, app) is IGridService childGrid)
    {
      childFormDesc = childGrid.GetCreateForms().FirstOrDefault();
      if (childFormDesc != null &&
          ModuleCatalog.Instance.CreateServiceInstance(childFormDesc.FormServiceType, db, app) is IFormService childForm)
      {
        childFormInst = childForm;
        var (cEls, _) = await childForm.GetElementsMetadataAsync(ct);
        childEditableElements = cEls.Where(AgentFeatureRegistry.IsEditableElement).ToList();
        foreach (var cel in childEditableElements)
        {
          if (cel.Model is FormSelectModel cs && !string.IsNullOrEmpty(cs.ReferenceService))
          {
            refServices.Add(cs.ReferenceService);
          }
        }
      }
    }

    var (dictionaries, _) = await AgentContextQueryService.LoadSmartDictionariesAsync(
      refServices,
      options.MaxFullDictionarySize,
      [],
      db,
      app,
      ct);

    // Ensure any existing/default reference IDs from large dictionaries (>50 rows) are loaded
    foreach (var el in editableElements)
    {
      if (el.Model is not FormSelectModel { ReferenceService: { Length: > 0 } refSvc } sel)
      {
        continue;
      }

      if (sel.IsMultiple || el.ValueKind == FormValueKind.List)
      {
        foreach (var existingId in ExtractLongList(formValues.GetValueOrDefault(el.Path)))
        {
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refSvc, existingId, dictionaries, db, app, ct);
        }
      }
      else if (formValues.TryGetValue(el.Path, out var curVal) && TryConvertToLong(curVal, out var curId) && curId > 0)
      {
        await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refSvc, curId, dictionaries, db, app, ct);
      }
    }

    var tagsMode = TryGetString(args, "tagsMode") ?? TryGetString(valuesSource, "tagsMode");
    var newReferences = new List<PendingNewReference>();
    var explicitlyProvided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var el in editableElements)
    {
      var select = el.Model as FormSelectModel;
      var hasProp = TryGetPropertyIgnoreCase(valuesSource, el.Path, out var rawProp) ||
                    hasNestedValues && TryGetPropertyIgnoreCase(args, el.Path, out rawProp);

      if (select != null && (select.IsMultiple || el.ValueKind == FormValueKind.List))
      {
        if (!hasProp && el.Path.EndsWith("Ids", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 3)
        {
          var baseMultiName = el.Path[..^3];
          foreach (var multiAlias in new[] { $"{baseMultiName}s", baseMultiName })
          {
            if (TryGetPropertyIgnoreCase(valuesSource, multiAlias, out rawProp) ||
                hasNestedValues && TryGetPropertyIgnoreCase(args, multiAlias, out rawProp))
            {
              hasProp = true;
              break;
            }
          }
        }

        if (hasProp && rawProp.ValueKind == JsonValueKind.Array)
        {
          var ids = isEdit && string.Equals(tagsMode, "append", StringComparison.OrdinalIgnoreCase)
            ? ExtractLongList(formValues.GetValueOrDefault(el.Path))
            : new List<long>();
          var refFormDesc = !string.IsNullOrEmpty(select.ReferenceService)
            ? AgentFeatureRegistry.ResolveReferenceCreateForm(descriptors, select.ReferenceService, el, db, app)
            : null;

          foreach (var item in rawProp.EnumerateArray())
          {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id) && id > 0)
            {
              if (!string.IsNullOrEmpty(select.ReferenceService))
              {
                await AgentContextQueryService.EnsureReferenceIdLoadedAsync(select.ReferenceService, id, dictionaries, db, app, ct);
              }

              if (!ids.Contains(id))
              {
                ids.Add(id);
              }
            }
            else if (item.ValueKind == JsonValueKind.Object && TryGetLong(item, "id") is > 0 and var objItemId)
            {
              if (!string.IsNullOrEmpty(select.ReferenceService))
              {
                await AgentContextQueryService.EnsureReferenceIdLoadedAsync(select.ReferenceService, objItemId, dictionaries, db, app, ct);
              }

              if (!ids.Contains(objItemId))
              {
                ids.Add(objItemId);
              }
            }
            else if (item.ValueKind == JsonValueKind.String && item.GetString()?.Trim() is { Length: > 0 } strName &&
                     !string.IsNullOrEmpty(select.ReferenceService))
            {
              if (long.TryParse(strName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) &&
                  await AgentContextQueryService.EnsureReferenceIdLoadedAsync(select.ReferenceService, parsedId, dictionaries, db, app, ct) != null)
              {
                if (!ids.Contains(parsedId))
                {
                  ids.Add(parsedId);
                }
              }
              else
              {
                var matched = await AgentContextQueryService.FindOrSearchReferenceRowAsync(
                  select.ReferenceService,
                  select.ReferenceFilter,
                  strName,
                  allowFuzzy: refFormDesc == null,
                  dictionaries,
                  db,
                  app,
                  ct);

                if (matched != null && TryGetRowId(matched, out var mId))
                {
                  if (!ids.Contains(mId))
                  {
                    ids.Add(mId);
                  }
                }
                else if (refFormDesc != null)
                {
                  var extra = ExtractEqualityFilterMap(select.ReferenceFilter);
                  newReferences.Add(new PendingNewReference
                  {
                    FieldPath = el.Path,
                    Name = strName,
                    FormService = refFormDesc.ServicePath,
                    IsMultiple = true,
                    ExtraValues = extra
                  });
                }
              }
            }
          }

          formValues[el.Path] = ids;
          explicitlyProvided.Add(el.Path);
        }
        else if (hasProp && rawProp.ValueKind == JsonValueKind.Null)
        {
          formValues[el.Path] = new List<long>();
          explicitlyProvided.Add(el.Path);
        }

        continue;
      }

      if (select != null && !string.IsNullOrEmpty(select.ReferenceService))
      {
        var refFormDesc = AgentFeatureRegistry.ResolveReferenceCreateForm(descriptors, select.ReferenceService, el, db, app);

        if (!hasProp && el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 2)
        {
          var baseRefName = el.Path[..^2];
          foreach (var refAlias in new[] { $"{baseRefName}Name", baseRefName })
          {
            if (TryGetPropertyIgnoreCase(valuesSource, refAlias, out rawProp) ||
                hasNestedValues && TryGetPropertyIgnoreCase(args, refAlias, out rawProp))
            {
              hasProp = true;
              break;
            }
          }
        }

        long? resolvedId = null;
        string? unresolvedName = null;
        Dictionary<string, object?>? objectExtraValues = null;

        if (hasProp)
        {
          if (rawProp.ValueKind == JsonValueKind.Number && rawProp.TryGetInt64(out var numId) && numId > 0)
          {
            await AgentContextQueryService.EnsureReferenceIdLoadedAsync(select.ReferenceService, numId, dictionaries, db, app, ct);
            var allDictRows = dictionaries.TryGetValue(select.ReferenceService, out var rows) ? rows : [];
            var filteredRows = FilterRowsByReferenceFilter(allDictRows, select.ReferenceFilter);

            if (filteredRows.Any(r => TryGetRowId(r, out var rid) && rid == numId))
            {
              resolvedId = numId;
            }
            else if (allDictRows.FirstOrDefault(r => TryGetRowId(r, out var rid) && rid == numId) is { } wrongKindRow)
            {
              var wrongName = GetRowDisplayName(wrongKindRow);
              var sameNameMatch = !string.IsNullOrEmpty(wrongName)
                ? await AgentContextQueryService.FindOrSearchReferenceRowAsync(
                    select.ReferenceService,
                    select.ReferenceFilter,
                    wrongName,
                    allowFuzzy: true,
                    dictionaries,
                    db,
                    app,
                    ct)
                : null;
              if (sameNameMatch != null && TryGetRowId(sameNameMatch, out var correctedId))
              {
                resolvedId = correctedId;
              }
            }
          }
          else if (rawProp.ValueKind == JsonValueKind.String && rawProp.GetString()?.Trim() is { Length: > 0 } strVal)
          {
            if (long.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) &&
                await AgentContextQueryService.EnsureReferenceIdLoadedAsync(select.ReferenceService, parsedId, dictionaries, db, app, ct) != null &&
                FilterRowsByReferenceFilter(dictionaries[select.ReferenceService], select.ReferenceFilter).Any(r => TryGetRowId(r, out var rid) && rid == parsedId))
            {
              resolvedId = parsedId;
            }
            else
            {
              var matched = await AgentContextQueryService.FindOrSearchReferenceRowAsync(
                select.ReferenceService,
                select.ReferenceFilter,
                strVal,
                allowFuzzy: refFormDesc == null,
                dictionaries,
                db,
                app,
                ct);
              if (matched != null && TryGetRowId(matched, out var mId))
              {
                resolvedId = mId;
              }
              else
              {
                unresolvedName = strVal;
              }
            }
          }
          else if (rawProp.ValueKind == JsonValueKind.Object)
          {
            if (TryGetLong(rawProp, "id") is > 0 and var objId &&
                await AgentContextQueryService.EnsureReferenceIdLoadedAsync(select.ReferenceService, objId, dictionaries, db, app, ct) != null &&
                FilterRowsByReferenceFilter(dictionaries[select.ReferenceService], select.ReferenceFilter).Any(r => TryGetRowId(r, out var rid) && rid == objId))
            {
              resolvedId = objId;
            }
            else if (TryGetString(rawProp, "name")?.Trim() is { Length: > 0 } objName)
            {
              var matched = await AgentContextQueryService.FindOrSearchReferenceRowAsync(
                select.ReferenceService,
                select.ReferenceFilter,
                objName,
                allowFuzzy: refFormDesc == null,
                dictionaries,
                db,
                app,
                ct);
              if (matched != null && TryGetRowId(matched, out var mId))
              {
                resolvedId = mId;
              }
              else
              {
                unresolvedName = objName;
                objectExtraValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in rawProp.EnumerateObject())
                {
                  if (!prop.Name.Equals("id", StringComparison.OrdinalIgnoreCase) &&
                      !prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                  {
                    objectExtraValues[prop.Name] = ConvertJsonValue(prop.Value, UiFieldType.Text);
                  }
                }
              }
            }
          }
        }

        // Also check explicit new-reference alias like "newCounterpartyName"
        if (resolvedId == null && unresolvedName == null && el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 2)
        {
          var baseName = el.Path[..^2];
          var pascalBase = char.ToUpperInvariant(baseName[0]) + baseName[1..];
          var newAlias = $"new{pascalBase}Name";
          if ((TryGetPropertyIgnoreCase(valuesSource, newAlias, out var aliasProp) ||
               TryGetPropertyIgnoreCase(args, newAlias, out aliasProp)) &&
              aliasProp.ValueKind == JsonValueKind.String &&
              aliasProp.GetString()?.Trim() is { Length: > 0 } aliasStr)
          {
            var matched = await AgentContextQueryService.FindOrSearchReferenceRowAsync(
              select.ReferenceService,
              select.ReferenceFilter,
              aliasStr,
              allowFuzzy: false,
              dictionaries,
              db,
              app,
              ct);
            if (matched != null && TryGetRowId(matched, out var mId))
            {
              resolvedId = mId;
            }
            else
            {
              unresolvedName = aliasStr;
            }
          }
        }

        if (resolvedId.HasValue)
        {
          formValues[el.Path] = resolvedId.Value;
          explicitlyProvided.Add(el.Path);
        }
        else if (!string.IsNullOrWhiteSpace(unresolvedName))
        {
          formValues[el.Path] = null;
          explicitlyProvided.Add(el.Path);
          var extra = ExtractEqualityFilterMap(select.ReferenceFilter);
          if (objectExtraValues != null)
          {
            foreach (var (k, v) in objectExtraValues)
            {
              extra[k] = v;
            }
          }

          newReferences.Add(new PendingNewReference
          {
            FieldPath = el.Path,
            Name = unresolvedName,
            FormService = refFormDesc?.ServicePath,
            IsMultiple = false,
            ExtraValues = extra
          });
        }
        else if (hasProp && rawProp.ValueKind == JsonValueKind.Null)
        {
          formValues[el.Path] = null;
          explicitlyProvided.Add(el.Path);
        }

        continue;
      }

      if (hasProp && rawProp.ValueKind != JsonValueKind.Undefined)
      {
        formValues[el.Path] = ConvertJsonValue(rawProp, el.Type);
        explicitlyProvided.Add(el.Path);
      }
    }

    // Apply declarative SelectPatches (e.g. Counterparty.DefaultCategory -> CategoryId) when target field was not explicitly set
    foreach (var el in editableElements)
    {
      if (isEdit && !explicitlyProvided.Contains(el.Path))
      {
        continue;
      }

      if (el.SelectPatches is not { Count: > 0 } patches ||
          el.Model is not FormSelectModel { ReferenceService: { Length: > 0 } refSvc } ||
          !formValues.TryGetValue(el.Path, out var curVal) ||
          !TryConvertToLong(curVal, out var selectedId) ||
          selectedId <= 0)
      {
        continue;
      }

      if (isEdit &&
          originalFormValues.TryGetValue(el.Path, out var oldSrcVal) &&
          TryConvertToLong(oldSrcVal, out var oldSrcId) &&
          oldSrcId == selectedId)
      {
        continue;
      }

      var selectedRow = await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refSvc, selectedId, dictionaries, db, app, ct);
      if (selectedRow == null)
      {
        continue;
      }

      foreach (var patch in patches)
      {
        if (explicitlyProvided.Contains(patch.TargetField))
        {
          continue;
        }

        if (newReferences.Any(nr => string.Equals(nr.FieldPath, patch.TargetField, StringComparison.OrdinalIgnoreCase)))
        {
          continue;
        }

        if ((!isEdit || patch.OnlyWhenEmpty) &&
            formValues.TryGetValue(patch.TargetField, out var existingTarget) &&
            existingTarget != null &&
            existingTarget.ToString() is { Length: > 0 } existingStr &&
            existingStr != "0")
        {
          continue;
        }

        if (selectedRow.TryGetValue(patch.SourceColumn, out var patchVal) && patchVal != null)
        {
          var unwrapped = UnwrapReferenceValue(patchVal);
          if (TryConvertToLong(unwrapped, out var patchId) && patchId > 0)
          {
            var targetEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, patch.TargetField, StringComparison.OrdinalIgnoreCase));
            if (targetEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } targetRefSvc } targetSelect)
            {
              await AgentContextQueryService.EnsureReferenceIdLoadedAsync(targetRefSvc, patchId, dictionaries, db, app, ct);
              if (dictionaries.TryGetValue(targetRefSvc, out var targetAllRows))
              {
                var targetFiltered = FilterRowsByReferenceFilter(targetAllRows, targetSelect.ReferenceFilter);
                if (targetFiltered.Count > 0 && !targetFiltered.Any(r => TryGetRowId(r, out var trId) && trId == patchId))
                {
                  continue;
                }
              }
            }

            formValues[patch.TargetField] = patchId;
          }
        }
      }
    }

    // Ensure required single-reference fields have a valid default if not provided and not pending creation
    foreach (var el in editableElements)
    {
      if (!el.IsRequired || el.Model is not FormSelectModel select || select.IsMultiple || el.ValueKind == FormValueKind.List)
      {
        continue;
      }

      if (newReferences.Any(nr => !nr.IsMultiple && string.Equals(nr.FieldPath, el.Path, StringComparison.OrdinalIgnoreCase)))
      {
        continue;
      }

      if (formValues.TryGetValue(el.Path, out var curVal) && TryConvertToLong(curVal, out var curId) && curId > 0)
      {
        continue;
      }

      if (select.Options is { Count: > 0 })
      {
        formValues[el.Path] = select.Options[0].Id;
      }
      else if (!string.IsNullOrEmpty(select.ReferenceService) &&
               dictionaries.TryGetValue(select.ReferenceService, out var allRows) &&
               allRows.Count > 0)
      {
        var candidateRows = FilterRowsByReferenceFilter(allRows, select.ReferenceFilter);
        var activeRow = candidateRows.FirstOrDefault(r => !r.TryGetValue("isActive", out var act) || act is not false)
          ?? candidateRows.FirstOrDefault();
        if (activeRow != null && TryGetRowId(activeRow, out var fallbackId) && fallbackId > 0)
        {
          formValues[el.Path] = fallbackId;
        }
      }
    }

    // Parse child line items universally if present
    var proposalLines = new List<AgentProposalLineDto>();
    var rawLineDicts = new List<Dictionary<string, object?>>();
    var linesMode = TryGetString(args, "linesMode") ?? TryGetString(valuesSource, "linesMode") ?? "replace";
    if (childFormDesc != null &&
        childFormInst != null &&
        (TryGetPropertyIgnoreCase(args, "lines", out var linesProp) ||
          TryGetPropertyIgnoreCase(valuesSource, "lines", out linesProp)) &&
        linesProp.ValueKind == JsonValueKind.Array)
    {
      var childDefaults = await AgentContextQueryService.GetDefaultFormValuesAsync(childFormInst, childEditableElements, ct);

      foreach (var item in linesProp.EnumerateArray())
      {
        if (item.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        var lineDict = new Dictionary<string, object?>(childDefaults, StringComparer.OrdinalIgnoreCase);
        string? lineCategoryLabel = null;

        foreach (var cel in childEditableElements)
        {
          var hasCellProp = TryGetPropertyIgnoreCase(item, cel.Path, out var rawCellProp);

          if (cel.Model is FormSelectModel cSelect && !string.IsNullOrEmpty(cSelect.ReferenceService))
          {
            long? resolvedCellId = null;

            if (hasCellProp)
            {
              if (rawCellProp.ValueKind == JsonValueKind.Number && rawCellProp.TryGetInt64(out var numId) && numId > 0)
              {
                await AgentContextQueryService.EnsureReferenceIdLoadedAsync(cSelect.ReferenceService, numId, dictionaries, db, app, ct);
                var cRows = FilterRowsByReferenceFilter(dictionaries.GetValueOrDefault(cSelect.ReferenceService) ?? [], cSelect.ReferenceFilter);
                if (cRows.Any(r => TryGetRowId(r, out var rid) && rid == numId))
                {
                  resolvedCellId = numId;
                }
              }
              else if (rawCellProp.ValueKind == JsonValueKind.String && rawCellProp.GetString()?.Trim() is { Length: > 0 } cStr)
              {
                if (long.TryParse(cStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) &&
                    await AgentContextQueryService.EnsureReferenceIdLoadedAsync(cSelect.ReferenceService, parsedId, dictionaries, db, app, ct) != null &&
                    FilterRowsByReferenceFilter(dictionaries[cSelect.ReferenceService], cSelect.ReferenceFilter).Any(r => TryGetRowId(r, out var rid) && rid == parsedId))
                {
                  resolvedCellId = parsedId;
                }
                else if (await AgentContextQueryService.FindOrSearchReferenceRowAsync(
                           cSelect.ReferenceService,
                           cSelect.ReferenceFilter,
                           cStr,
                           allowFuzzy: true,
                           dictionaries,
                           db,
                           app,
                           ct) is { } matchedRow &&
                         TryGetRowId(matchedRow, out var mId))
                {
                  resolvedCellId = mId;
                }
              }
            }

            if (resolvedCellId == null && cel.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
              var baseName = cel.Path[..^2];
              foreach (var alias in new[] { $"{baseName}Name", baseName })
              {
                if (TryGetPropertyIgnoreCase(item, alias, out var aliasProp) &&
                    aliasProp.ValueKind == JsonValueKind.String &&
                    aliasProp.GetString()?.Trim() is { Length: > 0 } aliasStr &&
                    await AgentContextQueryService.FindOrSearchReferenceRowAsync(
                      cSelect.ReferenceService,
                      cSelect.ReferenceFilter,
                      aliasStr,
                      allowFuzzy: true,
                      dictionaries,
                      db,
                      app,
                      ct) is { } matchedRow &&
                    TryGetRowId(matchedRow, out var mId))
                {
                  resolvedCellId = mId;
                  break;
                }
              }
            }

            if (resolvedCellId == null &&
                formValues.TryGetValue(cel.Path, out var parentRefVal) &&
                TryConvertToLong(parentRefVal, out var parentRefId) &&
                parentRefId > 0)
            {
              resolvedCellId = parentRefId;
            }

            lineDict[cel.Path] = resolvedCellId;

            if (lineCategoryLabel == null)
            {
              if (resolvedCellId is > 0)
              {
                lineCategoryLabel = ResolveSingleName(resolvedCellId.Value, cSelect, dictionaries);
              }
              else if (newReferences.FirstOrDefault(nr => string.Equals(nr.FieldPath, cel.Path, StringComparison.OrdinalIgnoreCase)) is { } pendingParentRef)
              {
                lineCategoryLabel = pendingParentRef.Name;
              }
            }

            continue;
          }

          if (hasCellProp && rawCellProp.ValueKind != JsonValueKind.Undefined)
          {
            lineDict[cel.Path] = ConvertJsonValue(rawCellProp, cel.Type);
          }
        }

        var name = lineDict.TryGetValue("name", out var nameObj) ? nameObj?.ToString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
          var firstTextEl = childEditableElements.FirstOrDefault(e => e.Type == UiFieldType.Text && e.Model is not FormSelectModel);
          if (firstTextEl != null && lineDict.TryGetValue(firstTextEl.Path, out var altObj))
          {
            name = altObj?.ToString()?.Trim();
          }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
          continue;
        }

        var qty = TryGetDecimal(item, "qty") is > 0 and var q
          ? q
          : (lineDict.TryGetValue("qty", out var defQ) && TryConvertToDecimal(defQ, out var dq) && dq > 0 ? dq : 1m);
        var price = TryGetDecimal(item, "price");
        var amount = TryGetDecimal(item, "amount");

        if (price is null or 0m && amount is not null and not 0m)
        {
          price = qty > 0 ? Math.Round(amount.Value / qty, 2) : amount.Value;
        }

        if (amount is null or 0m && price is not null and not 0m)
        {
          amount = Math.Round(qty * price.Value, 2);
        }

        var finalPrice = price ?? 0m;
        var finalAmount = amount ?? 0m;

        if (childEditableElements.Any(e => e.Path.Equals("qty", StringComparison.OrdinalIgnoreCase)))
        {
          lineDict["qty"] = qty;
        }

        if (childEditableElements.Any(e => e.Path.Equals("price", StringComparison.OrdinalIgnoreCase)))
        {
          lineDict["price"] = finalPrice;
        }

        if (childEditableElements.Any(e => e.Path.Equals("amount", StringComparison.OrdinalIgnoreCase)))
        {
          lineDict["amount"] = finalAmount;
        }

        proposalLines.Add(new AgentProposalLineDto
        {
          Name = name,
          Qty = qty,
          Price = finalPrice,
          Amount = finalAmount,
          Category = lineCategoryLabel
        });

        rawLineDicts.Add(lineDict);
      }

      var primaryMoneyEl = editableElements.FirstOrDefault(e => e.Model is FormMoneyModel);
      if (proposalLines.Count > 0 &&
          primaryMoneyEl != null &&
          (!explicitlyProvided.Contains(primaryMoneyEl.Path) || !isEdit && formValues.TryGetValue(primaryMoneyEl.Path, out var curAmt) && (!TryConvertToDecimal(curAmt, out var decAmt) || decAmt <= 0m)))
      {
        var linesTotal = proposalLines.Sum(l => l.Amount);
        formValues[primaryMoneyEl.Path] = isEdit && string.Equals(linesMode, "append", StringComparison.OrdinalIgnoreCase) &&
            formValues.TryGetValue(primaryMoneyEl.Path, out var baseAmtObj) && TryConvertToDecimal(baseAmtObj, out var baseAmt) && baseAmt > 0m
          ? baseAmt + linesTotal
          : linesTotal;
      }
    }

    // Stage prerequisite proposal cards for new references (independent references first, then references with SelectPatches)
    var orderedNewRefs = newReferences
      .OrderBy(nr => editableElements.FirstOrDefault(e => string.Equals(e.Path, nr.FieldPath, StringComparison.OrdinalIgnoreCase))?.SelectPatches?.Count ?? 0)
      .ToList();

    foreach (var pendingRef in orderedNewRefs)
    {
      if (string.IsNullOrEmpty(pendingRef.FormService))
      {
        continue;
      }

      var sourceEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, pendingRef.FieldPath, StringComparison.OrdinalIgnoreCase));
      if (sourceEl?.SelectPatches is { Count: > 0 } patches)
      {
        foreach (var patch in patches)
        {
          var sourceIdKey = $"{patch.SourceColumn}Id";
          if (formValues.TryGetValue(patch.TargetField, out var targetVal) && TryConvertToLong(targetVal, out var targetId) && targetId > 0)
          {
            pendingRef.ExtraValues.TryAdd(sourceIdKey, targetId);
            pendingRef.ExtraValues.TryAdd(patch.SourceColumn, targetId);
          }
          else if (newReferences.FirstOrDefault(nr => string.Equals(nr.FieldPath, patch.TargetField, StringComparison.OrdinalIgnoreCase)) is { } targetPendingRef)
          {
            pendingRef.ExtraValues.TryAdd(sourceIdKey, targetPendingRef.Name);
            pendingRef.ExtraValues.TryAdd(patch.SourceColumn, targetPendingRef.Name);
          }
        }
      }

      if (sourceEl?.Model is FormSelectModel { ReferenceDraftRules: { Count: > 0 } draftRules })
      {
        foreach (var rule in draftRules)
        {
          if ((!isEdit || explicitlyProvided.Contains(rule.DraftField)) &&
              formValues.TryGetValue(rule.DraftField, out var draftVal) &&
              draftVal != null)
          {
            pendingRef.ExtraValues.TryAdd(rule.SourceField, draftVal);
          }
        }
      }

      var existingPrereq = store.GetSessionProposals(sessionId, owner).FirstOrDefault(p =>
        string.Equals(p.Service, pendingRef.FormService, StringComparison.OrdinalIgnoreCase) &&
        p.Title.Contains(pendingRef.Name, StringComparison.OrdinalIgnoreCase));

      if (existingPrereq != null)
      {
        pendingRef.ProposalId = existingPrereq.Id;
      }
      else
      {
        var prereqValues = new Dictionary<string, object?>(pendingRef.ExtraValues, StringComparer.OrdinalIgnoreCase)
        {
          ["name"] = pendingRef.Name
        };

        var prereqArgs = JsonSerializer.SerializeToElement(new
        {
          sessionId,
          service = pendingRef.FormService,
          values = prereqValues
        }, WebJson);

        try
        {
          var stagedPrereq = await StageProposalAsync(prereqArgs, services, ct);
          pendingRef.ProposalId = stagedPrereq.Id;
        }
        catch { }
      }
    }

    // Collect attachment IDs
    var attachmentIds = new List<string>();
    if (children.Any(c => c.IsFiles))
    {
      if ((TryGetPropertyIgnoreCase(args, "attachmentIds", out var attProp) ||
           TryGetPropertyIgnoreCase(valuesSource, "attachmentIds", out attProp)) &&
          attProp.ValueKind == JsonValueKind.Array)
      {
        foreach (var item in attProp.EnumerateArray())
        {
          if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } aid)
          {
            attachmentIds.Add(aid);
          }
        }
      }

      if (attachmentIds.Count == 0 && fallbackAttachmentIds is { Count: > 0 })
      {
        attachmentIds.AddRange(fallbackAttachmentIds);
      }
    }

    // Build localized display fields for the proposal card
    var displayFields = BuildDisplayFields(
      editableElements,
      originalFormValues,
      formValues,
      newReferences,
      explicitlyProvided,
      dictionaries,
      isEdit,
      L,
      out var currencyLabel);

    var baseTitle = BuildProposalTitle(targetDesc, formValues, newReferences, editableElements, currencyLabel);
    var title = isEdit ? $"#{recordId!.Value} · {baseTitle}" : baseTitle;
    var subtitle = BuildProposalSubtitle(formValues, newReferences, editableElements, dictionaries);

    // Deduplicate if an identical pending proposal is already staged in this session
    var existingDuplicate = store.GetSessionProposals(sessionId, owner).FirstOrDefault(p =>
      p.Status == "pending" &&
      p.RecordId == recordId &&
      string.Equals(p.Service, targetDesc.ServicePath, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(p.Subtitle, subtitle, StringComparison.OrdinalIgnoreCase) &&
      p.AttachmentIds.Count == attachmentIds.Count &&
      p.AttachmentIds.SequenceEqual(attachmentIds, StringComparer.Ordinal) &&
      p.Lines.Count == proposalLines.Count &&
      p.Lines.Zip(proposalLines).All(pair =>
        string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
        pair.First.Qty == pair.Second.Qty &&
        pair.First.Price == pair.Second.Price &&
        pair.First.Amount == pair.Second.Amount &&
        string.Equals(pair.First.Category, pair.Second.Category, StringComparison.Ordinal)) &&
      p.Fields.Count == displayFields.Count &&
      p.Fields.Zip(displayFields).All(pair =>
        string.Equals(pair.First.Label, pair.Second.Label, StringComparison.Ordinal) &&
        string.Equals(pair.First.Value, pair.Second.Value, StringComparison.Ordinal)));

    if (existingDuplicate != null)
    {
      return existingDuplicate;
    }

    var (dupWarning, dupRecordId, dupFormService) = isEdit
      ? (null, (long?)null, null)
      : await DetectDuplicateRecordAsync(
          formInst,
          targetDesc,
          descriptors,
          editableElements,
          children.FirstOrDefault(c => c.IsFiles && c.ServiceType != null)?.ServiceType,
          formValues,
          attachmentIds,
          currencyLabel,
          store,
          owner,
          db,
          app,
          L,
          ct);

    var payloadElement = JsonSerializer.SerializeToElement(new StagedProposalPayload
    {
      Service = targetDesc.ServicePath,
      RecordId = recordId,
      Values = formValues,
      NewReferences = orderedNewRefs,
      ChildFormService = childFormDesc?.ServicePath,
      HasLinesUpdate = proposalLines.Count > 0,
      LinesMode = linesMode,
      Lines = rawLineDicts
    }, WebJson);

    var proposal = new AgentProposalDto
    {
      Id = Guid.NewGuid().ToString("N")[..12],
      SessionId = sessionId,
      Kind = targetDesc.ServicePath,
      Service = targetDesc.ServicePath,
      RecordId = recordId,
      Title = title,
      Subtitle = subtitle,
      Badge = isEdit ? L["AgentEditBadge"] : targetDesc.Label,
      BadgeColor = isEdit ? UiColor.Amber : (targetDesc.Color ?? UiColor.Blue),
      Fields = displayFields,
      Lines = proposalLines,
      LinesLabel = proposalLines.Count > 0 ? L["AgentLinesTitle", proposalLines.Count] : null,
      AttachmentIds = attachmentIds,
      Payload = payloadElement,
      FormService = targetDesc.ServicePath,
      Overlay = formInst.Overlay(targetDesc.ServicePath),
      DuplicateWarning = dupWarning,
      DuplicateRecordId = dupRecordId,
      DuplicateFormService = dupFormService
    };

    store.StageProposal(proposal, owner);
    return proposal;
  }
}
