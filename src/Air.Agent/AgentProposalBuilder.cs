using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static partial class AgentProposalBuilder
{
  private static AgentProposalDto? FindReferencedProposal(
    AgentStore store,
    string sessionId,
    AgentOwnerScope owner,
    string? turnId,
    string refKey,
    string? referenceService
  )
  {
    var sessionProposals = store.GetSessionProposals(sessionId, owner)
      .Where(p => p.Status is "pending" or "committed")
      .ToList();

    bool IsCompatible(AgentProposalDto p) =>
      string.IsNullOrWhiteSpace(referenceService) ||
      AgentStepGraphHelper.AreServicesEntityCompatible(referenceService, p.Service);

    if (!string.IsNullOrWhiteSpace(turnId))
    {
      var sameTurn = sessionProposals.LastOrDefault(p =>
        string.Equals(p.TurnId, turnId, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(p.StepKey, refKey, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(p.Id, refKey, StringComparison.OrdinalIgnoreCase)) &&
        IsCompatible(p)
      );
      if (sameTurn != null)
      {
        return sameTurn;
      }

      return sessionProposals.LastOrDefault(p =>
        string.Equals(p.Id, refKey, StringComparison.OrdinalIgnoreCase) && IsCompatible(p)
      );
    }

    return sessionProposals.LastOrDefault(p =>
      (p.TurnId == null && string.Equals(p.StepKey, refKey, StringComparison.OrdinalIgnoreCase) ||
       string.Equals(p.Id, refKey, StringComparison.OrdinalIgnoreCase)) &&
      IsCompatible(p)
    );
  }

  public static async Task<AgentProposalDto> StageProposalAsync(
    JsonElement args,
    IServiceProvider services,
    CancellationToken ct,
    IReadOnlyList<string>? fallbackAttachmentIds = null,
    string? turnId = null
  )
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
      ?? bridge?.GetContext(argSessionId, owner.TenantId, owner.UserId)?.SessionId
      ?? "default"
    );

    var descriptors = AgentFeatureRegistry.DiscoverForms(db, app);
    if (descriptors.Count == 0)
    {
      throw new InvalidOperationException("No agent-enabled forms are available for the current user.");
    }

    var hasNestedValues = args.TryGetProperty("values", out var vProp) && vProp.ValueKind == JsonValueKind.Object;
    var valuesSource = hasNestedValues ? vProp : args;
    var stepKey = AgentStepGraphHelper.TryExtractStepKey(args);

    var recordId = TryGetRecordId(args) ?? (hasNestedValues ? TryGetRecordId(vProp) : null);
    var isEdit = recordId is > 0;

    var requestedService = TryGetString(args, "service") ?? TryGetString(args, "kind");
    var targetDesc = AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requestedService, db, app) ?? descriptors[0];
    if (isEdit)
    {
      targetDesc = await AgentFeatureRegistry.ResolveEditFormDescriptorAsync(
        descriptors,
        targetDesc,
        requestedService,
        recordId!.Value,
        db,
        app,
        ct
      );
    }
    else if (valuesSource.ValueKind == JsonValueKind.Object &&
             ModuleCatalog.Instance.CreateServiceInstance(targetDesc.FormServiceType, db, app) is IFormService initialForm &&
             initialForm.Feature.ServiceType != null &&
             typeof(IGridService).IsAssignableFrom(initialForm.Feature.ServiceType) &&
             ModuleCatalog.Instance.CreateServiceInstance(initialForm.Feature.ServiceType, db, app) is IGridService ownerGrid)
    {
      var siblingCreates = ownerGrid.GetAgentForms();
      var explicitlyMatchedSibling = !string.IsNullOrWhiteSpace(requestedService) &&
                                     siblingCreates.Any(c =>
                                       string.Equals(c.ServicePath, requestedService, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(
                                         AgentFeatureRegistry.NormalizeServiceKey(c.ServicePath),
                                         AgentFeatureRegistry.NormalizeServiceKey(requestedService),
                                         StringComparison.OrdinalIgnoreCase
                                       ) ||
                                       string.Equals(c.Label, requestedService, StringComparison.OrdinalIgnoreCase)
                                     );
      if (siblingCreates.Count > 1 && !explicitlyMatchedSibling)
      {
        var providedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in valuesSource.EnumerateObject())
        {
          if (prop.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
          {
            providedKeys.Add(prop.Name);
          }
        }

        if (providedKeys.Count > 0)
        {
          var bestScore = -1;
          GridCreateDescriptor? bestSibling = null;
          foreach (var candidate in siblingCreates)
          {
            if (ModuleCatalog.Instance.CreateServiceInstance(candidate.FormServiceType, db, app) is not IFormService candForm)
            {
              continue;
            }

            var (candEls, _) = await candForm.GetElementsMetadataAsync(ct);
            var candPaths = candEls
              .Where(AgentFeatureRegistry.IsEditableElement)
              .Select(e => e.Path)
              .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var score = providedKeys.Count(candPaths.Contains);
            if (score > bestScore)
            {
              bestScore = score;
              bestSibling = candidate;
            }
          }

          if (bestSibling != null && bestScore > 0)
          {
            targetDesc = descriptors.FirstOrDefault(d => string.Equals(
                             d.ServicePath,
                             bestSibling.ServicePath,
                             StringComparison.OrdinalIgnoreCase
                           )
                         )
                         ?? bestSibling;
          }
        }
      }
    }

    if (ModuleCatalog.Instance.CreateServiceInstance(targetDesc.FormServiceType, db, app) is not IFormService formInst ||
        !AgentFeatureRegistry.CanEditFeature(formInst.Feature, app))
    {
      throw new InvalidOperationException($"Access denied or form service '{targetDesc.ServicePath}' is unavailable.");
    }

    var initialDraft = await formInst.PrepareDraftAsync(isEdit ? recordId : null, ct: ct);
    var originalFormValues = initialDraft.OriginalValues;
    var formValues = initialDraft.Values;
    var recordVer = initialDraft.Ver;

    var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
    var editableElements = elements.Where(AgentFeatureRegistry.IsEditableElement).ToList();

    var refServices = editableElements
      .Select(e => (e.Model as FormSelectModel)?.ReferenceService)
      .Where(s => !string.IsNullOrEmpty(s))
      .Cast<string>()
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var resolvedChildLine = AgentFeatureRegistry.ResolveEditableChildLineGrid(children, db, app);
    var childFormDesc = resolvedChildLine?.ChildCreate;
    var childFormInst = resolvedChildLine?.ChildForm;
    List<FormElementDto> childEditableElements = [];
    if (childFormInst != null)
    {
      childFormInst.SetRequest(
        new FormRequest
        {
          ParentId = isEdit ? recordId : null,
          ParentService = targetDesc.ServicePath
        }
      );
      var (cEls, _) = await childFormInst.GetElementsMetadataAsync(ct);
      childEditableElements = cEls.Where(AgentFeatureRegistry.IsEditableElement).ToList();
      foreach (var cel in childEditableElements)
      {
        if (cel.Model is FormSelectModel cs && !string.IsNullOrEmpty(cs.ReferenceService))
        {
          refServices.Add(cs.ReferenceService);
        }
      }
    }

    var (dictionaries, _) = await AgentContextQueryService.LoadSmartDictionariesAsync(
      refServices,
      options.MaxFullDictionarySize,
      [],
      db,
      app,
      ct
    );

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
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(
            refSvc,
            existingId,
            dictionaries,
            db,
            app,
            ct
          );
        }
      }
      else if (formValues.TryGetValue(el.Path, out var curVal) && TryConvertToLong(curVal, out var curId) && curId > 0)
      {
        await AgentContextQueryService.EnsureReferenceIdLoadedAsync(
          refSvc,
          curId,
          dictionaries,
          db,
          app,
          ct
        );
      }
    }

    var multiSelectMode = TryGetString(args, "multiSelectMode")
                          ?? TryGetString(valuesSource, "multiSelectMode")
                          ?? TryGetString(args, "tagsMode")
                          ?? TryGetString(valuesSource, "tagsMode");
    var newReferences = new List<PendingNewReference>();
    var explicitlyProvided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var linkedSelectPatches = new List<(FormElementDto Element, AgentProposalDto TargetProposal)>();

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
          var ids = isEdit && string.Equals(multiSelectMode, "append", StringComparison.OrdinalIgnoreCase)
            ? ExtractLongList(formValues.GetValueOrDefault(el.Path))
            : new List<long>();
          var refFormDesc = !string.IsNullOrEmpty(select.ReferenceService)
            ? AgentFeatureRegistry.ResolveReferenceCreateForm(
              descriptors,
              select.ReferenceService,
              el,
              db,
              app
            )
            : null;

          foreach (var item in rawProp.EnumerateArray())
          {
            if (AgentStepGraphHelper.TryExtractRefKey(item, out var multiRefKey))
            {
              var multiTarget = FindReferencedProposal(
                                  store,
                                  sessionId,
                                  owner,
                                  turnId,
                                  multiRefKey,
                                  select.ReferenceService
                                )
                                ?? throw new InvalidOperationException(L["MutationRefused"]);
              newReferences.Add(
                new PendingNewReference
                {
                  FieldPath = el.Path,
                  Name = AgentStepGraphHelper.GetProposalShortLabel(multiTarget),
                  FormService = multiTarget.Service,
                  IsMultiple = true,
                  ProposalId = multiTarget.Id,
                  StepKey = multiRefKey,
                  StepNumber = multiTarget.StepNumber,
                  IsExistingRecordEdit = multiTarget.RecordId is > 0
                }
              );

              continue;
            }

            var (itemId, unresolvedItemName, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
              item,
              select.ReferenceService ?? string.Empty,
              select.ReferenceFilter,
              allowFuzzy: refFormDesc == null,
              allowWrongKindFallback: false,
              dictionaries,
              db,
              app,
              ct,
              select.ReferenceLabelPaths
            );
            if (itemId is > 0 and var id)
            {
              if (!ids.Contains(id))
              {
                ids.Add(id);
              }
            }
            else if (!string.IsNullOrWhiteSpace(unresolvedItemName) && refFormDesc != null)
            {
              newReferences.Add(
                new PendingNewReference
                {
                  FieldPath = el.Path,
                  Name = unresolvedItemName,
                  FormService = refFormDesc.ServicePath,
                  IsMultiple = true,
                  ExtraValues = ExtractEqualityFilterMap(select.ReferenceFilter)
                }
              );
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
        var refFormDesc = AgentFeatureRegistry.ResolveReferenceCreateForm(
          descriptors,
          select.ReferenceService,
          el,
          db,
          app
        );

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

        if (hasProp && AgentStepGraphHelper.TryExtractRefKey(rawProp, out var singleRefKey))
        {
          var singleTarget = FindReferencedProposal(
                               store,
                               sessionId,
                               owner,
                               turnId,
                               singleRefKey,
                               select.ReferenceService
                             )
                             ?? throw new InvalidOperationException(L["MutationRefused"]);
          formValues[el.Path] = null;
          explicitlyProvided.Add(el.Path);
          newReferences.Add(
            new PendingNewReference
            {
              FieldPath = el.Path,
              Name = AgentStepGraphHelper.GetProposalShortLabel(singleTarget),
              FormService = singleTarget.Service,
              IsMultiple = false,
              ProposalId = singleTarget.Id,
              StepKey = singleRefKey,
              StepNumber = singleTarget.StepNumber,
              IsExistingRecordEdit = singleTarget.RecordId is > 0
            }
          );
          if (el.SelectPatches is { Count: > 0 })
          {
            linkedSelectPatches.Add((el, singleTarget));
          }

          continue;
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
            ct,
            select.ReferenceLabelPaths
          );
        }

        // Also check explicit new-reference alias like "new<Field>Name"
        if (resolvedId == null && unresolvedName == null && el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
            el.Path.Length > 2)
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
              ct,
              select.ReferenceLabelPaths
            );
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

          newReferences.Add(
            new PendingNewReference
            {
              FieldPath = el.Path,
              Name = unresolvedName,
              FormService = refFormDesc?.ServicePath,
              IsMultiple = false,
              ExtraValues = extra
            }
          );
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

    // Propagate declarative SelectPatches from linked prerequisite proposals (when target fields were not explicitly overridden)
    var pendingRefPaths = newReferences
      .Where(nr => nr.LineIndex == null)
      .Select(nr => nr.FieldPath)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var (sourceEl, targetProp) in linkedSelectPatches)
    {
      if (sourceEl.SelectPatches is not { Count: > 0 } patches || targetProp.Payload.ValueKind != JsonValueKind.Object)
      {
        continue;
      }

      StagedProposalPayload? targetPayload = null;
      try
      {
        targetPayload = targetProp.Payload.Deserialize<StagedProposalPayload>(WebJson);
      }
      catch { }

      if (targetPayload == null)
      {
        continue;
      }

      foreach (var patch in patches)
      {
        if (explicitlyProvided.Contains(patch.TargetField) || pendingRefPaths.Contains(patch.TargetField))
        {
          continue;
        }

        if (patch.OnlyWhenEmpty &&
            formValues.TryGetValue(patch.TargetField, out var existingTarget) &&
            existingTarget != null &&
            !(TryConvertToDecimal(existingTarget, out var existingDec) && existingDec == 0m) &&
            existingTarget.ToString() is { Length: > 0 } existingStr &&
            existingStr != "0")
        {
          continue;
        }

        var sourceIdKey = $"{patch.SourceColumn}Id";
        if ((targetPayload.Values.TryGetValue(sourceIdKey, out var patchedVal) ||
             targetPayload.Values.TryGetValue(patch.SourceColumn, out patchedVal)) &&
            patchedVal != null &&
            !(patchedVal is JsonElement { ValueKind: JsonValueKind.Null }))
        {
          var unwrapped = UnwrapReferenceValue(patchedVal);
          if (unwrapped == null)
          {
            continue;
          }

          var targetEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, patch.TargetField, StringComparison.OrdinalIgnoreCase));
          if (targetEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } targetRefSvc } targetSel &&
              TryConvertToLong(unwrapped, out var patchedId) &&
              patchedId > 0)
          {
            await AgentContextQueryService.EnsureReferenceIdLoadedAsync(
              targetRefSvc,
              patchedId,
              dictionaries,
              db,
              app,
              ct
            );
            if (targetSel.ReferenceFilter != null &&
                dictionaries.TryGetValue(targetRefSvc, out var tRows) &&
                !FilterRowsByReferenceFilter(tRows, targetSel.ReferenceFilter).Any(r => TryGetRowId(r, out var rid) && rid == patchedId))
            {
              continue;
            }

            formValues[patch.TargetField] = patchedId;
            explicitlyProvided.Add(patch.TargetField);
          }
          else if (targetEl != null && targetEl.Model is not FormSelectModel)
          {
            formValues[patch.TargetField] = unwrapped;
            explicitlyProvided.Add(patch.TargetField);
          }
        }
      }
    }

    // Delegate declarative SelectPatches, Options defaults, and hooks.Compute to FormService.PrepareDraftAsync
    var draftOverrides = explicitlyProvided
      .Where(k => !pendingRefPaths.Contains(k))
      .ToDictionary(
        k => k,
        k => formValues.GetValueOrDefault(k),
        StringComparer.OrdinalIgnoreCase
      );
    var preparedDraft = await formInst.PrepareDraftAsync(
      isEdit ? recordId : null,
      draftOverrides,
      pendingRefPaths,
      ct: ct
    );

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
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(
            patchedRefSvc,
            patchedId,
            dictionaries,
            db,
            app,
            ct
          );
        }
      }
    }

    // Parse child line items universally if present
    var proposalLines = new List<AgentProposalLineDto>();
    var rawLineDicts = new List<Dictionary<string, object?>>();
    var linesMode = TryGetString(args, "linesMode") ?? TryGetString(valuesSource, "linesMode") ?? "replace";
    var hasLinesProp = (TryGetPropertyIgnoreCase(args, "lines", out var linesProp) ||
                        TryGetPropertyIgnoreCase(valuesSource, "lines", out linesProp)) &&
                       linesProp.ValueKind == JsonValueKind.Array;
    if (childFormDesc != null &&
        childFormInst != null &&
        hasLinesProp)
    {
      (proposalLines, rawLineDicts) = await ParseChildLinesAsync(
        linesProp,
        linesMode,
        isEdit,
        recordId,
        targetDesc,
        resolvedChildLine?.ChildGrid,
        childFormInst,
        childEditableElements,
        editableElements,
        formValues,
        explicitlyProvided,
        newReferences,
        dictionaries,
        store,
        sessionId,
        owner,
        turnId,
        db,
        app,
        L,
        ct
      );
    }

    // Stage prerequisite proposal cards for new references (independent references first, then references with SelectPatches)
    var orderedNewRefs = newReferences
      .OrderBy(nr => nr.LineIndex.HasValue ? 1 : 0)
      .ThenBy(nr => nr.LineIndex ?? editableElements.FirstOrDefault(e => string.Equals(e.Path, nr.FieldPath, StringComparison.OrdinalIgnoreCase))?.SelectPatches
          ?.Count ?? 0
      )
      .ToList();

    foreach (var pendingRef in orderedNewRefs)
    {
      if (pendingRef.LineIndex != null ||
          !string.IsNullOrEmpty(pendingRef.ProposalId) ||
          string.IsNullOrEmpty(pendingRef.FormService))
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
          else if (newReferences.FirstOrDefault(nr =>
                     nr.LineIndex == null && string.Equals(nr.FieldPath, patch.TargetField, StringComparison.OrdinalIgnoreCase)
                   ) is { } targetPendingRef)
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
        p.Status == "pending" &&
        (turnId == null || string.Equals(p.TurnId, turnId, StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(p.Service, pendingRef.FormService, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(AgentStepGraphHelper.GetProposalShortLabel(p), pendingRef.Name, StringComparison.OrdinalIgnoreCase)
      );

      if (existingPrereq != null)
      {
        pendingRef.ProposalId = existingPrereq.Id;
        pendingRef.StepNumber ??= existingPrereq.StepNumber;
        pendingRef.StepKey ??= existingPrereq.StepKey;
      }
      else
      {
        var primaryTextKey = "name";
        var refFormType = ModuleCatalog.Instance.FindServiceType(pendingRef.FormService);
        if (refFormType != null &&
            ModuleCatalog.Instance.CreateServiceInstance(refFormType, db, app) is IFormService refFormInst)
        {
          var (refEls, _) = await refFormInst.GetElementsMetadataAsync(ct);
          primaryTextKey = ResolvePrimaryTextFieldPath(refEls, (sourceEl?.Model as FormSelectModel)?.ReferenceLabelPaths);
        }

        var prereqValues = new Dictionary<string, object?>(pendingRef.ExtraValues, StringComparer.OrdinalIgnoreCase)
        {
          [primaryTextKey] = pendingRef.Name
        };

        var prereqArgs = JsonSerializer.SerializeToElement(
          new
          {
            sessionId,
            service = pendingRef.FormService,
            values = prereqValues
          },
          WebJson
        );

        try
        {
          var stagedPrereq = await StageProposalAsync(prereqArgs, services, ct, turnId: turnId);
          pendingRef.ProposalId = stagedPrereq.Id;
          pendingRef.StepNumber = stagedPrereq.StepNumber;
        }
        catch { }
      }
    }

    var dependsOnIds = orderedNewRefs
      .Select(nr => nr.ProposalId)
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Select(id => id!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    if ((TryGetPropertyIgnoreCase(args, "dependsOn", out var depProp) ||
         hasNestedValues && TryGetPropertyIgnoreCase(vProp, "dependsOn", out depProp)) &&
        depProp.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in depProp.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.String &&
            AgentStepGraphHelper.NormalizeStepKey(item.GetString()) is { } depStepKey)
        {
          var depTarget = FindReferencedProposal(
                            store,
                            sessionId,
                            owner,
                            turnId,
                            depStepKey,
                            null
                          )
                          ?? throw new InvalidOperationException(L["MutationRefused"]);
          if (!dependsOnIds.Contains(depTarget.Id, StringComparer.OrdinalIgnoreCase))
          {
            dependsOnIds.Add(depTarget.Id);
          }
        }
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
      orderedNewRefs,
      explicitlyProvided,
      dictionaries,
      isEdit,
      L,
      out var currencyLabel
    );

    var baseTitle = BuildProposalTitle(
      targetDesc,
      formValues,
      orderedNewRefs,
      editableElements,
      currencyLabel
    );
    var title = isEdit ? $"#{recordId!.Value} · {baseTitle}" : baseTitle;
    var subtitle = BuildProposalSubtitle(formValues, orderedNewRefs, editableElements, dictionaries);

    // Deduplicate if an identical pending proposal is already staged in this session
    var existingDuplicate = store.GetSessionProposals(sessionId, owner).FirstOrDefault(p =>
      p.Status == "pending" &&
      (turnId == null || string.Equals(p.TurnId, turnId, StringComparison.OrdinalIgnoreCase)) &&
      string.Equals(p.StepKey, stepKey, StringComparison.OrdinalIgnoreCase) &&
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
        string.Equals(pair.First.Category, pair.Second.Category, StringComparison.Ordinal)
      ) &&
      p.Fields.Count == displayFields.Count &&
      p.Fields.Zip(displayFields).All(pair =>
        string.Equals(pair.First.Label, pair.Second.Label, StringComparison.Ordinal) &&
        string.Equals(pair.First.Value, pair.Second.Value, StringComparison.Ordinal)
      )
    );

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
        ct
      );

    var payloadElement = JsonSerializer.SerializeToElement(
      new StagedProposalPayload
      {
        Service = targetDesc.ServicePath,
        RecordId = recordId,
        Ver = recordVer,
        StepKey = stepKey,
        Values = formValues,
        ExplicitFields = [.. explicitlyProvided],
        DependsOnProposalIds = dependsOnIds,
        NewReferences = orderedNewRefs,
        ChildFormService = childFormDesc?.ServicePath,
        HasLinesUpdate = isEdit ? hasLinesProp && childFormDesc != null : proposalLines.Count > 0,
        LinesMode = linesMode,
        Lines = rawLineDicts
      },
      WebJson
    );

    var proposal = new AgentProposalDto
    {
      Id = Guid.NewGuid().ToString("N")[..12],
      SessionId = sessionId,
      Kind = targetDesc.ServicePath,
      Service = targetDesc.ServicePath,
      RecordId = recordId,
      StepKey = stepKey,
      DependsOn = dependsOnIds.Count > 0 ? dependsOnIds : null,
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
      Overlay = await formInst.OverlayAsync(targetDesc.ServicePath, ct),
      DuplicateWarning = dupWarning,
      DuplicateRecordId = dupRecordId,
      DuplicateFormService = dupFormService,
      TurnId = turnId
    };

    store.StageProposal(proposal, owner);
    return proposal;
  }
}
