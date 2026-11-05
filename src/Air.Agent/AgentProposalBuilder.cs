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

    var argSessionId = TryGetString(args, "sessionId");
    var sessionId = AgentStore.NormalizeSessionId(
      argSessionId
      ?? bridge?.GetContext(argSessionId)?.SessionId
      ?? "default");

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

    var initialDraft = await formInst.PrepareDraftAsync(isEdit ? recordId : null, ct: ct);
    var originalFormValues = initialDraft.OriginalValues;
    var formValues = initialDraft.Values;
    var recordVer = initialDraft.Ver;

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
            var (itemId, unresolvedItemName, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
              item,
              select.ReferenceService ?? string.Empty,
              select.ReferenceFilter,
              allowFuzzy: refFormDesc == null,
              allowWrongKindFallback: false,
              dictionaries,
              db,
              app,
              ct);
            if (itemId is > 0 and var id)
            {
              if (!ids.Contains(id))
              {
                ids.Add(id);
              }
            }
            else if (!string.IsNullOrWhiteSpace(unresolvedItemName) && refFormDesc != null)
            {
              newReferences.Add(new PendingNewReference
              {
                FieldPath = el.Path,
                Name = unresolvedItemName,
                FormService = refFormDesc.ServicePath,
                IsMultiple = true,
                ExtraValues = ExtractEqualityFilterMap(select.ReferenceFilter)
              });
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
          (resolvedId, unresolvedName, objectExtraValues) = await AgentReferenceResolver.ResolveReferenceValueAsync(
            rawProp,
            select.ReferenceService,
            select.ReferenceFilter,
            allowFuzzy: refFormDesc == null,
            allowWrongKindFallback: true,
            dictionaries,
            db,
            app,
            ct);
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

    // Delegate declarative SelectPatches, Options defaults, and hooks.Compute to FormService.PrepareDraftAsync
    var draftOverrides = explicitlyProvided.ToDictionary(
      k => k,
      k => formValues.GetValueOrDefault(k),
      StringComparer.OrdinalIgnoreCase);
    var pendingRefPaths = newReferences
      .Select(nr => nr.FieldPath)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var preparedDraft = await formInst.PrepareDraftAsync(
      isEdit ? recordId : null,
      draftOverrides,
      pendingRefPaths,
      ct: ct);

    foreach (var (k, v) in preparedDraft.Values)
    {
      if (!explicitlyProvided.Contains(k) && !pendingRefPaths.Contains(k) && v != null)
      {
        formValues[k] = v;
        var patchedEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, k, StringComparison.OrdinalIgnoreCase));
        if (patchedEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } patchedRefSvc } &&
            TryConvertToLong(v, out var patchedId) &&
            patchedId > 0)
        {
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(patchedRefSvc, patchedId, dictionaries, db, app, ct);
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
      var childDefaults = await AgentContextQueryService.GetDefaultFormValuesAsync(childFormInst, ct);

      foreach (var item in linesProp.EnumerateArray())
      {
        if (item.ValueKind != JsonValueKind.Object)
        {
          continue;
        }

        var lineDict = new Dictionary<string, object?>(childDefaults, StringComparer.OrdinalIgnoreCase);
        var lineRefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var explicitlySetLineFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cel in childEditableElements)
        {
          var hasCellProp = TryGetPropertyIgnoreCase(item, cel.Path, out var rawCellProp);

          if (cel.Model is FormSelectModel cSelect && !string.IsNullOrEmpty(cSelect.ReferenceService))
          {
            long? resolvedCellId = null;
            string? resolvedCellName = null;

            if (hasCellProp)
            {
              explicitlySetLineFields.Add(cel.Path);
              (resolvedCellId, resolvedCellName, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
                rawCellProp,
                cSelect.ReferenceService,
                cSelect.ReferenceFilter,
                allowFuzzy: true,
                allowWrongKindFallback: false,
                dictionaries,
                db,
                app,
                ct);
            }

            if (resolvedCellId == null && cel.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
              var baseName = cel.Path[..^2];
              foreach (var alias in new[] { $"{baseName}Name", baseName })
              {
                if (TryGetPropertyIgnoreCase(item, alias, out var aliasProp))
                {
                  explicitlySetLineFields.Add(cel.Path);
                  (resolvedCellId, resolvedCellName, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
                    aliasProp,
                    cSelect.ReferenceService,
                    cSelect.ReferenceFilter,
                    allowFuzzy: true,
                    allowWrongKindFallback: false,
                    dictionaries,
                    db,
                    app,
                    ct);
                  if (resolvedCellId != null)
                  {
                    break;
                  }
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

            if (resolvedCellId is > 0)
            {
              var refLabel = !string.IsNullOrWhiteSpace(resolvedCellName)
                ? resolvedCellName
                : ResolveSingleName(resolvedCellId.Value, cSelect, dictionaries);
              if (!string.IsNullOrWhiteSpace(refLabel))
              {
                lineRefLabels[cel.Path] = refLabel;
              }
            }
            else if (newReferences.FirstOrDefault(nr => string.Equals(nr.FieldPath, cel.Path, StringComparison.OrdinalIgnoreCase)) is { } pendingParentRef)
            {
              lineRefLabels[cel.Path] = pendingParentRef.Name;
            }

            continue;
          }

          if (hasCellProp && rawCellProp.ValueKind != JsonValueKind.Undefined)
          {
            explicitlySetLineFields.Add(cel.Path);
            lineDict[cel.Path] = ConvertJsonValue(rawCellProp, cel.Type);
          }
        }

        var usedLinePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "qty", "price", "amount", "sortOrder" };
        string? name = null;
        if (lineDict.TryGetValue("name", out var nameObj) && !string.IsNullOrWhiteSpace(nameObj?.ToString()))
        {
          name = nameObj.ToString()!.Trim();
          usedLinePaths.Add("name");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
          var firstTextEl = childEditableElements.FirstOrDefault(e => e.Type == UiFieldType.Text && e.Model is not FormSelectModel);
          if (firstTextEl != null && lineDict.TryGetValue(firstTextEl.Path, out var altObj) && !string.IsNullOrWhiteSpace(altObj?.ToString()))
          {
            name = altObj.ToString()!.Trim();
            usedLinePaths.Add(firstTextEl.Path);
          }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
          var firstRefEl = childEditableElements.FirstOrDefault(e => e.Model is FormSelectModel && lineRefLabels.ContainsKey(e.Path));
          if (firstRefEl != null && lineRefLabels.TryGetValue(firstRefEl.Path, out var refLabel))
          {
            name = refLabel;
            usedLinePaths.Add(firstRefEl.Path);
          }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
          continue;
        }

        string? lineCategoryLabel = null;
        var secondaryRefEl = childEditableElements.FirstOrDefault(e =>
          e.Model is FormSelectModel && !usedLinePaths.Contains(e.Path) && lineRefLabels.ContainsKey(e.Path));
        if (secondaryRefEl != null && lineRefLabels.TryGetValue(secondaryRefEl.Path, out var secLabel))
        {
          lineCategoryLabel = secLabel;
          usedLinePaths.Add(secondaryRefEl.Path);
        }

        var lineDraft = await childFormInst.PrepareDraftAsync(
          recordId: null,
          overrides: lineDict,
          parentId: isEdit ? recordId : null,
          parentService: targetDesc.ServicePath,
          ct: ct);
        foreach (var (k, v) in lineDraft.Values)
        {
          if (v != null)
          {
            lineDict[k] = v;
          }
        }

        var qty = TryGetDecimal(item, "qty") is > 0 and var q
          ? q
          : (lineDict.TryGetValue("qty", out var defQ) && TryConvertToDecimal(defQ, out var dq) && dq > 0 ? dq : 1m);
        var price = TryGetDecimal(item, "price")
          ?? (lineDict.TryGetValue("price", out var defP) && TryConvertToDecimal(defP, out var dp) && dp != 0m ? dp : null);
        var amount = TryGetDecimal(item, "amount")
          ?? (lineDict.TryGetValue("amount", out var defA) && TryConvertToDecimal(defA, out var da) && da != 0m ? da : null);

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

        List<AgentProposalFieldDto>? extraLineFields = null;
        foreach (var cel in childEditableElements)
        {
          if (usedLinePaths.Contains(cel.Path) || !explicitlySetLineFields.Contains(cel.Path))
          {
            continue;
          }

          string? formattedVal = null;
          if (lineRefLabels.TryGetValue(cel.Path, out var extraRefLabel))
          {
            formattedVal = extraRefLabel;
          }
          else if (lineDict.TryGetValue(cel.Path, out var rawExtra) && rawExtra != null)
          {
            if (TryConvertToDecimal(rawExtra, out var extraDec))
            {
              if (extraDec != 0m)
              {
                formattedVal = cel.Model is FormPercentModel
                  ? $"{extraDec.ToString("0.##", CultureInfo.InvariantCulture)}%"
                  : extraDec.ToString("0.##", CultureInfo.InvariantCulture);
              }
            }
            else if (rawExtra is bool extraBool)
            {
              if (extraBool)
              {
                formattedVal = L["Yes"];
              }
            }
            else
            {
              var s = rawExtra.ToString()?.Trim();
              if (!string.IsNullOrWhiteSpace(s))
              {
                formattedVal = s;
              }
            }
          }

          if (!string.IsNullOrWhiteSpace(formattedVal))
          {
            extraLineFields ??= [];
            extraLineFields.Add(new AgentProposalFieldDto
            {
              Label = cel.Label ?? cel.Path,
              Value = formattedVal
            });
          }
        }

        proposalLines.Add(new AgentProposalLineDto
        {
          Name = name,
          Qty = qty,
          Price = finalPrice,
          Amount = finalAmount,
          Category = lineCategoryLabel,
          Fields = extraLineFields
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
      Ver = recordVer,
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
