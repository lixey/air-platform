using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalActionExecutor
{
  public static async Task<AgentStepCommitOutcome> ExecuteRunActionStepAsync(
    AgentProposalDto proposal,
    StagedProposalPayload payload,
    HashSet<(Type EntityType, long RecordId)> preVerifiedEdits,
    IReadOnlyList<AgentModuleScope> scopes,
    IReadOnlyList<string> refreshServices,
    Localizer L,
    CancellationToken mutationCt)
  {
    var serviceType = ModuleCatalog.Instance.FindServiceType(payload.Service);
    var feature = serviceType != null ? ModuleCatalog.Instance.FindFeature(serviceType) : null;
    if (serviceType == null || feature is not { AllowsAgent: true } || string.IsNullOrWhiteSpace(payload.Action))
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    var stepScope = AgentModuleScope.SelectScopeForService(scopes, serviceType, scopes[0]);
    if (!AgentFeatureRegistry.CanViewFeature(feature, stepScope.App))
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    var targetIds = ResolveTargetIds(payload);

    JsonElement? askJson = payload.Values.Count > 0 ? JsonSerializer.SerializeToElement(payload.Values, WebJson) : null;
    DeclaredActionPreviewDto livePreview;
    try
    {
      livePreview = await RunActionPreviewCoreAsync(serviceType, payload.Action, targetIds, askJson, null, stepScope.Db, stepScope.App, mutationCt)
        ?? throw AgentRefusal.Create(L["MutationRefused"]);
    }
    catch (FormRefusedException refusedEx)
    {
      throw AgentRefusal.Create(refusedEx.Messages.FirstOrDefault()?.Detail ?? refusedEx.Message, refusedEx);
    }

    List<int>? versions = payload.SelectedVersions != null ? [.. payload.SelectedVersions] : null;
    var entityType = ModuleCatalog.GetServiceEntityType(serviceType);
    if (versions != null &&
        livePreview.TargetVersions != null &&
        versions.Count == livePreview.TargetVersions.Count &&
        entityType != null)
    {
      for (var i = 0; i < targetIds.Count && i < versions.Count; i++)
      {
        if (versions[i] != livePreview.TargetVersions[i] && preVerifiedEdits.Contains((entityType, targetIds[i])))
        {
          versions[i] = livePreview.TargetVersions[i];
        }
      }
    }
    else if (versions == null && livePreview.TargetVersions != null)
    {
      versions = [.. livePreview.TargetVersions];
    }

    long? openId;
    string? openScreen;
    List<MessageDto>? warnings;
    string? noticeTitle;
    List<MessageDto>? noticeMessages;

    if (typeof(IGridService).IsAssignableFrom(serviceType) &&
        ModuleCatalog.Instance.CreateServiceInstance(serviceType, stepScope.Db, stepScope.App) is IGridService gridInst)
    {
      var res = await gridInst.ExecuteDeclaredActionAsync(
        payload.Action,
        new GridRequest { SelectedIds = targetIds, SelectedVersions = versions },
        askJson,
        mutationCt) ?? throw AgentRefusal.Create(L["MutationRefused"]);

      if (res.Outcome != MutationOutcome.Committed)
      {
        var fallback = res.Outcome == MutationOutcome.Conflict ? L["FormChangedElsewhere"] : L["MutationRefused"];
        throw AgentRefusal.Create(res.Messages?.FirstOrDefault()?.Detail ?? fallback);
      }

      openId = res.OpenId;
      openScreen = res.OpenScreen;
      warnings = res.Warnings;
      noticeTitle = res.Title;
      noticeMessages = res.Messages is { Count: > 0 } ? res.Messages : null;
    }
    else if (typeof(IFormService).IsAssignableFrom(serviceType) &&
             ModuleCatalog.Instance.CreateServiceInstance(serviceType, stepScope.Db, stepScope.App) is IFormService formInst)
    {
      var targetId = targetIds.FirstOrDefault();
      var ver = versions is { Count: > 0 } ? versions[0] : payload.Ver;
      var res = await formInst.ExecuteDeclaredActionAsync(
        payload.Action,
        new FormRequest { Id = targetId > 0 ? targetId : null, Ver = ver, Value = askJson },
        askJson,
        mutationCt) ?? throw AgentRefusal.Create(L["MutationRefused"]);

      if (res.Outcome != MutationOutcome.Committed)
      {
        var fallback = res.Outcome == MutationOutcome.Conflict ? L["FormChangedElsewhere"] : L["MutationRefused"];
        throw AgentRefusal.Create(res.Messages?.FirstOrDefault()?.Detail ?? fallback);
      }

      openId = res.OpenId;
      openScreen = res.OpenScreen;
      warnings = res.Warnings;
      noticeTitle = res.Title;
      noticeMessages = res.Messages is { Count: > 0 } ? res.Messages : null;
    }
    else
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    var finalId = openId is > 0 ? openId.Value : targetIds.FirstOrDefault();
    var finalFormPath = ResolveCommittedFormPath(openScreen, proposal.FormService, serviceType, payload.Service, finalId);
    var label = ResolveCommittedLabel(proposal.Title, finalId, livePreview.Label);

    var allRefresh = refreshServices.ToList();
    if (!string.IsNullOrWhiteSpace(openScreen) &&
        ModuleCatalog.Instance.FindServiceType(openScreen) is { } openedType &&
        ModuleCatalog.Instance.FindFeature(openedType) is { } openedFeat &&
        openedFeat.ServiceType != null &&
        typeof(IGridService).IsAssignableFrom(openedFeat.ServiceType))
    {
      var openedGrid = ServicePathResolver.Resolve(openedFeat.ServiceType);
      if (!allRefresh.Contains(openedGrid, StringComparer.OrdinalIgnoreCase))
      {
        allRefresh.Add(openedGrid);
      }
    }

    return new AgentStepCommitOutcome(finalId, label, finalFormPath, allRefresh, warnings, noticeTitle, noticeMessages);
  }

  public static async Task<DeclaredActionPreviewDto?> RunActionPreviewCoreAsync(
    Type serviceType,
    string handle,
    IReadOnlyList<long> targetIds,
    JsonElement? askJson,
    IReadOnlySet<string>? skipValidationPaths,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    if (typeof(IGridService).IsAssignableFrom(serviceType) &&
        ModuleCatalog.Instance.CreateServiceInstance(serviceType, db, app) is IGridService gridInst)
    {
      return await gridInst.PreviewDeclaredActionAsync(handle, targetIds, askJson, skipValidationPaths, requireAutomationOptIn: true, ct);
    }

    if (typeof(IFormService).IsAssignableFrom(serviceType) &&
        ModuleCatalog.Instance.CreateServiceInstance(serviceType, db, app) is IFormService formInst)
    {
      return await formInst.PreviewDeclaredActionAsync(handle, targetIds, askJson, skipValidationPaths, requireAutomationOptIn: true, ct);
    }

    return null;
  }

  private static List<long> ResolveTargetIds(StagedProposalPayload payload)
  {
    if (payload.SelectedIds.Count > 0)
    {
      return payload.SelectedIds.Where(id => id > 0).ToList();
    }

    return payload.RecordId is > 0 ? [payload.RecordId.Value] : [];
  }

  private static string ResolveCommittedFormPath(
    string? openScreen,
    string? proposalFormService,
    Type serviceType,
    string payloadService,
    long finalId)
  {
    if (!string.IsNullOrWhiteSpace(openScreen) &&
        ModuleCatalog.Instance.FindServiceType(openScreen) is { } openType &&
        typeof(IFormService).IsAssignableFrom(openType))
    {
      return openScreen;
    }

    if (!string.IsNullOrWhiteSpace(proposalFormService))
    {
      return proposalFormService;
    }

    if (finalId > 0 && typeof(IFormService).IsAssignableFrom(serviceType))
    {
      return payloadService;
    }

    return string.Empty;
  }

  private static string ResolveCommittedLabel(string? proposalTitle, long finalId, string previewLabel)
  {
    if (!string.IsNullOrWhiteSpace(proposalTitle))
    {
      return proposalTitle;
    }

    return finalId > 0 ? $"#{finalId}" : previewLabel;
  }
}
