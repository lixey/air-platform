using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalActionBuilder
{
  public static async Task<AgentProposalDto> StageRunActionProposalAsync(
    ProposalBuildContext ctx,
    JsonElement args,
    string requestedAction,
    string? requestedService,
    CancellationToken ct)
  {
    var valuesSource = TryGetPropertyIgnoreCase(args, "values", out var vProp) && vProp.ValueKind == JsonValueKind.Object ? vProp : args;
    var stepKey = AgentStepGraphHelper.TryExtractStepKey(args);

    var (feature, targetServiceType, fallbackFormType) = ResolveActionTargetService(requestedService);
    if (feature is not { AllowsAgent: true } || targetServiceType == null)
    {
      throw new InvalidOperationException($"Action service '{requestedService}' is not available or not enabled for AI.");
    }

    await using var scope = AgentModuleScope.OpenForService(ctx.Services, targetServiceType, ctx.Db, ctx.App);
    if (!AgentFeatureRegistry.CanViewFeature(feature, scope.App))
    {
      throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
    }

    var (activeServiceType, matched) = await FindPermittedActionAsync(
      targetServiceType, feature, fallbackFormType, requestedAction, scope.Db, scope.App, ct);
    if (activeServiceType == null || matched == null)
    {
      throw new InvalidOperationException($"Action '{requestedAction}' is not available or not enabled for AI on '{requestedService}'.");
    }

    var newReferences = new List<PendingNewReference>();
    var previewTargetIds = ResolveActionTargets(ctx, args, valuesSource, matched.SelectionMode, matched.Service, newReferences);
    var askEditable = matched.AskElements.Where(AgentFeatureRegistry.IsEditableElement).ToList();

    var (askValues, explicitAskFields, skipValidationPaths, dictionaries) = await AgentReferenceResolver.ParseAskInputsAsync(
      ctx, args, valuesSource, askEditable, newReferences, scope.Db, scope.App, ct);

    var askJson = askValues.Count > 0 ? JsonSerializer.SerializeToElement(askValues, WebJson) : (JsonElement?)null;
    DeclaredActionPreviewDto preview;
    try
    {
      preview = await AgentProposalActionExecutor.RunActionPreviewCoreAsync(
                  activeServiceType, matched.Handle, previewTargetIds, askJson, skipValidationPaths, scope.Db, scope.App, ct)
                ?? throw new InvalidOperationException($"Action '{matched.Handle}' is not available.");
    }
    catch (FormRefusedException refusedEx)
    {
      throw new InvalidOperationException(FormatRefusedException(refusedEx, matched.AskElements), refusedEx);
    }

    foreach (var (k, v) in preview.AskValues)
    {
      if (!explicitAskFields.Contains(k) && !skipValidationPaths.Contains(k) && v != null)
      {
        askValues[k] = v;
        var patchedEl = askEditable.FirstOrDefault(e => string.Equals(e.Path, k, StringComparison.OrdinalIgnoreCase));
        if (patchedEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } patchedRefSvc } &&
            TryConvertToLong(v, out var patchedId) && patchedId > 0)
        {
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(patchedRefSvc, patchedId, dictionaries, scope.Db, scope.App, ct);
        }
      }
    }

    var dependsOnIds = newReferences
      .Select(nr => nr.ProposalId)
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Select(id => id!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    var fields = AgentProposalDisplayBuilder.BuildDisplayFields(
      askEditable, [], askValues, newReferences, explicitAskFields, dictionaries, isEdit: false, ctx.Localizer);

    var primaryId = previewTargetIds.Count == 1 && previewTargetIds[0] > 0 ? previewTargetIds[0] : (long?)null;
    var (title, subtitle) = BuildActionTitleAndSubtitle(preview, previewTargetIds, newReferences);

    var formServicePath = fallbackFormType != null ? ServicePathResolver.Resolve(fallbackFormType) : null;
    OverlayDto? overlay = null;
    if (fallbackFormType != null &&
        formServicePath != null &&
        primaryId is > 0 &&
        ModuleCatalog.Instance.CreateServiceInstance(fallbackFormType, scope.Db, scope.App) is IFormService formInst)
    {
      overlay = await formInst.OverlayAsync(formServicePath, ct);
    }

    var payload = JsonSerializer.SerializeToElement(
      new StagedProposalPayload
      {
        Operation = AgentProposalOperation.RunAction,
        Service = preview.Service,
        Action = preview.Handle,
        RecordId = primaryId,
        Ver = preview.TargetVersions is { Count: 1 } ? preview.TargetVersions[0] : null,
        SelectedIds = [.. previewTargetIds],
        SelectedVersions = preview.TargetVersions?.ToList(),
        StepKey = stepKey,
        Values = askValues,
        ExplicitFields = [.. explicitAskFields],
        DependsOnProposalIds = dependsOnIds,
        NewReferences = newReferences
      },
      WebJson);

    var warningTexts = preview.Warnings
      .Select(w => w.Detail ?? w.Title)
      .Where(d => !string.IsNullOrWhiteSpace(d))
      .Select(d => d!)
      .Distinct(StringComparer.Ordinal)
      .ToList();

    var proposal = new AgentProposalDto
    {
      Id = Guid.NewGuid().ToString("N")[..12],
      SessionId = ctx.SessionId,
      Operation = AgentProposalOperation.RunAction,
      Service = preview.Service,
      Action = preview.Handle,
      RecordId = primaryId,
      StepKey = stepKey,
      DependsOn = dependsOnIds.Count > 0 ? dependsOnIds : null,
      Title = title,
      Subtitle = subtitle,
      Badge = preview.Label,
      BadgeColor = preview.Color ?? UiColor.Blue,
      Fields = fields,
      Warnings = warningTexts.Count > 0 ? warningTexts : null,
      Payload = payload,
      FormService = primaryId is > 0 ? formServicePath : null,
      Overlay = overlay,
      TurnId = ctx.TurnId
    };

    ctx.Store.StageProposal(proposal, ctx.Owner);
    return proposal;
  }

  private static List<long> ResolveActionTargets(
    ProposalBuildContext ctx,
    JsonElement args,
    JsonElement valuesSource,
    string selectionMode,
    string servicePath,
    List<PendingNewReference> newReferences)
  {
    if (string.Equals(selectionMode, "none", StringComparison.OrdinalIgnoreCase))
    {
      return [];
    }

    if ((TryGetPropertyIgnoreCase(args, "selectedIds", out var selEl) ||
         !valuesSource.Equals(args) && TryGetPropertyIgnoreCase(valuesSource, "selectedIds", out selEl)) &&
        selEl.ValueKind == JsonValueKind.Array)
    {
      var targets = new List<long>();
      var idx = 0;
      foreach (var item in selEl.EnumerateArray())
      {
        if (AgentStepGraphHelper.TryExtractRefKey(item, out var itemRefKey))
        {
          var refTarget = ctx.FindReferencedProposal(itemRefKey)
            ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
          targets.Add(refTarget.RecordId is > 0 ? refTarget.RecordId.Value : -(idx + 1));
          newReferences.Add(new PendingNewReference
          {
            FieldPath = selectionMode == "single" ? "__targetId" : $"__selectedId:{idx}",
            Name = AgentStepGraphHelper.GetProposalShortLabel(refTarget),
            FormService = refTarget.Service,
            ProposalId = refTarget.Id,
            StepKey = itemRefKey,
            StepNumber = refTarget.StepNumber,
            IsExistingRecordEdit = refTarget.RecordId is > 0
          });
        }
        else if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var lid) && lid > 0)
        {
          targets.Add(lid);
        }
        else if (item.ValueKind == JsonValueKind.String &&
                 long.TryParse(item.GetString()?.Trim().TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid) &&
                 sid > 0)
        {
          targets.Add(sid);
        }

        idx++;
      }

      return targets;
    }

    foreach (var idPropName in new[] { "id", "recordId" })
    {
      if ((TryGetPropertyIgnoreCase(args, idPropName, out var rawIdEl) ||
           !valuesSource.Equals(args) && TryGetPropertyIgnoreCase(valuesSource, idPropName, out rawIdEl)) &&
          AgentStepGraphHelper.TryExtractRefKey(rawIdEl, out var singleRefKey))
      {
        var refTarget = ctx.FindReferencedProposal(singleRefKey, servicePath)
          ?? ctx.FindReferencedProposal(singleRefKey)
          ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
        newReferences.Add(new PendingNewReference
        {
          FieldPath = "__targetId",
          Name = AgentStepGraphHelper.GetProposalShortLabel(refTarget),
          FormService = refTarget.Service,
          ProposalId = refTarget.Id,
          StepKey = singleRefKey,
          StepNumber = refTarget.StepNumber,
          IsExistingRecordEdit = refTarget.RecordId is > 0
        });
        return [refTarget.RecordId is > 0 ? refTarget.RecordId.Value : -1L];
      }
    }

    var directId = TryGetRecordId(args) ?? (!valuesSource.Equals(args) ? TryGetRecordId(valuesSource) : null);
    return directId is > 0 ? [directId.Value] : [];
  }

  private static async Task<(Type? ServiceType, DeclaredActionDescriptorDto? Action)> FindPermittedActionAsync(
    Type targetServiceType,
    Feature feature,
    Type? fallbackFormType,
    string requestedAction,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    var candidates = new List<Type> { targetServiceType };
    if (feature.ServiceType != null && !candidates.Contains(feature.ServiceType))
    {
      candidates.Add(feature.ServiceType);
    }

    if (fallbackFormType != null && !candidates.Contains(fallbackFormType))
    {
      candidates.Add(fallbackFormType);
    }

    foreach (var candidateType in candidates)
    {
      IReadOnlyList<DeclaredActionDescriptorDto> list = [];
      if (typeof(IGridService).IsAssignableFrom(candidateType) &&
          ModuleCatalog.Instance.CreateServiceInstance(candidateType, db, app) is IGridService g)
      {
        list = await g.DescribeDeclaredActionsAsync(automationOnly: true, ct);
      }
      else if (typeof(IFormService).IsAssignableFrom(candidateType) &&
               ModuleCatalog.Instance.CreateServiceInstance(candidateType, db, app) is IFormService f)
      {
        list = await f.DescribeDeclaredActionsAsync(automationOnly: true, ct);
      }

      var matched = list.FirstOrDefault(a =>
        string.Equals(a.Handle, requestedAction, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(a.Method, requestedAction, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(a.Label, requestedAction, StringComparison.OrdinalIgnoreCase));
      if (matched != null)
      {
        return (candidateType, matched);
      }
    }

    return (null, null);
  }

  private static (string Title, string? Subtitle) BuildActionTitleAndSubtitle(
    DeclaredActionPreviewDto preview,
    IReadOnlyList<long> targetIds,
    IReadOnlyList<PendingNewReference> newReferences)
  {
    if (targetIds.Count == 1)
    {
      var id = targetIds[0];
      var caption = preview.TargetCaptions.FirstOrDefault()
        ?? newReferences.FirstOrDefault(r => r.FieldPath.StartsWith("__", StringComparison.Ordinal))?.Name;
      if (id > 0)
      {
        return !string.IsNullOrWhiteSpace(caption)
          ? ($"#{id} · {caption}", preview.Label)
          : ($"#{id} · {preview.Label}", null);
      }

      return !string.IsNullOrWhiteSpace(caption) ? ($"{preview.Label}: {caption}", null) : (preview.Label, null);
    }

    if (targetIds.Count > 1)
    {
      var summary = string.Join(", ", targetIds.Where(id => id > 0).Take(3).Select(id => $"#{id}"));
      return !string.IsNullOrWhiteSpace(summary) ? ($"{preview.Label} ({targetIds.Count})", summary) : ($"{preview.Label} ({targetIds.Count})", null);
    }

    return (preview.Label, null);
  }

  internal static (Feature? Feature, Type? TargetServiceType, Type? FallbackFormType) ResolveActionTargetService(string? requestedService)
  {
    if (string.IsNullOrWhiteSpace(requestedService))
    {
      return (null, null, null);
    }

    var directType = ModuleCatalog.Instance.FindServiceType(requestedService.Trim());
    if (directType == null)
    {
      var norm = AgentFeatureRegistry.NormalizeServiceKey(requestedService);
      directType = ModuleCatalog.Instance.AllServiceTypes.FirstOrDefault(t =>
        !t.IsAbstract &&
        string.Equals(AgentFeatureRegistry.NormalizeServiceKey(ServicePathResolver.Resolve(t)), norm, StringComparison.OrdinalIgnoreCase));
    }

    var feature = directType != null ? ModuleCatalog.Instance.FindFeature(directType) : null;
    if (feature == null)
    {
      return (null, null, null);
    }

    var formType = directType != null && typeof(IFormService).IsAssignableFrom(directType)
      ? directType
      : ModuleCatalog.Instance.AllServiceTypes.FirstOrDefault(t =>
          !t.IsAbstract && typeof(IFormService).IsAssignableFrom(t) && ModuleCatalog.Instance.FindFeature(t) == feature);

    return (feature, directType, formType);
  }
}
