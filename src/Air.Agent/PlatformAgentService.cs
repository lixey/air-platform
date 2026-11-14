using Microsoft.AspNetCore.Mvc;
using Platform.Agent.Internals;
using Platform.Api.Mutations.Internals;

namespace Platform.Agent;

[ServiceRoute("agent")]
public sealed class PlatformAgentService(PlatformAppService? app = null) : PlatformService(app)
{
  private static readonly Feature AgentFeature = new("Platform", "Agent", typeof(PlatformAgentService));

  public override Feature Feature => AgentFeature;

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(AgentStatusDto))]
  public async Task<IResult> GetStatus(
    HttpContext httpContext,
    [FromServices] AgentStore store,
    IEnumerable<IAgentProvider> providers,
    IEnumerable<IAgentSyncProvider> syncProviders,
    IAgentModelSynchronizer synchronizer,
    [FromServices] AgentOptions options,
    CancellationToken ct = default)
  {
    var enabledProviders = providers
      .Where(p => options.EnabledProviders.Count == 0 || options.EnabledProviders.Contains(p.Handle))
      .ToList();

    List<AgentModelDto> models = [];
    var defaultModel = options.DefaultModel;
    string? photoModel = null;

    if (AgentEndpointHandlers.HasDatabaseRegistered(httpContext.RequestServices))
    {
      await using var dbScope = AgentModuleScope.OpenDefault(httpContext.RequestServices);
      List<AppAgentModel> dbModels;
      (enabledProviders, dbModels) = await AgentEndpointHandlers.LoadActiveProvidersAndModelsAsync(
        dbScope.Db, store, synchronizer, options, enabledProviders, ct);

      var availableHandles = enabledProviders
        .Where(static p => p.IsAvailable)
        .Select(static p => p.Handle)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      if (availableHandles.Count > 0)
      {
        dbModels = [.. dbModels.Where(m => availableHandles.Contains(m.ProviderHandle))];
      }

      var multiProvider = dbModels.Select(static m => m.ProviderHandle).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
      models = [.. dbModels.Select(m => new AgentModelDto
      {
        Id = m.ModelId,
        Name = multiProvider ? $"{m.Name} [{m.ProviderHandle}]" : m.Name,
        Provider = m.ProviderHandle,
        IsDefault = m.IsDefault,
        IsForPhotos = m.IsForPhotos
      })];

      var defRow = dbModels.FirstOrDefault(static m => m.IsDefault) ?? dbModels.FirstOrDefault();
      defaultModel = defRow?.ModelId ?? options.DefaultModel;
      photoModel = (dbModels.FirstOrDefault(static m => m.IsForPhotos) ?? defRow)?.ModelId;
    }

    var providerDtos = enabledProviders
      .Select(static p => new AgentProviderDto
      {
        Handle = p.Handle,
        Name = p.Name,
        IsAvailable = p.IsAvailable
      })
      .ToList();

    var activeProvider = enabledProviders.FirstOrDefault(p =>
                           string.Equals(p.Handle, options.DefaultProvider, StringComparison.OrdinalIgnoreCase) && p.IsAvailable)
                         ?? enabledProviders.FirstOrDefault(static p => p.IsAvailable)
                         ?? enabledProviders.FirstOrDefault();

    if (models.FirstOrDefault(static m => m.IsDefault) is { } defModelDto)
    {
      activeProvider = enabledProviders.FirstOrDefault(p =>
        string.Equals(p.Handle, defModelDto.Provider, StringComparison.OrdinalIgnoreCase)) ?? activeProvider;
    }

    var syncSources = new List<AgentSyncSourceDto>();
    foreach (var sp in syncProviders)
    {
      try
      {
        var provided = await sp.GetSourcesAsync(httpContext.RequestServices, ct);
        if (provided.Count > 0)
        {
          syncSources.AddRange(provided);
        }
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
      }
    }

    return Results.Ok(new AgentStatusDto
    {
      ActiveProvider = activeProvider?.Handle ?? options.DefaultProvider,
      DefaultModel = defaultModel,
      PhotoModel = photoModel,
      Providers = providerDtos,
      Models = models,
      SyncSources = syncSources
    });
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(MutationResultDto))]
  public IResult Reset(
    AgentResetRequest? request,
    HttpContext httpContext,
    [FromServices] AgentStore store)
  {
    if (request is not null && AgentStore.IsValidSessionId(request.SessionId))
    {
      store.ResetSession(request.SessionId!, AgentStore.ResolveOwner(httpContext.RequestServices));
    }

    return Results.Ok(new MutationResultDto { Outcome = MutationOutcome.Committed });
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(AgentCommitResultDto))]
  public Task<IResult> ConfirmProposal(
    ConfirmProposalRequest request,
    HttpContext httpContext,
    [FromServices] AgentStore store,
    CancellationToken ct = default)
  {
    var proposalId = request?.ProposalId?.Trim() ?? string.Empty;
    var owner = AgentStore.ResolveOwner(httpContext.RequestServices);

    return RunIdempotentAsync<AgentCommitResultDto>(
      httpContext,
      owner,
      "agent/confirm-proposal",
      proposalId,
      async innerCt =>
      {
        if (string.IsNullOrEmpty(proposalId))
        {
          return (null, RefusedBadRequest(L["RequestInvalid"]));
        }

        AgentProposalDto? proposal;
        try
        {
          proposal = store.GetVerifiedProposal(proposalId, owner);
        }
        catch (InvalidOperationException ex) when (AgentRefusal.IsDomainRefusal(ex))
        {
          return (null, RefusedUnprocessable(ex.Message));
        }

        if (proposal is null)
        {
          return (null, RefusedNotFound(L["AgentProposalNotFound"]));
        }

        if (proposal.Status == "superseded")
        {
          return (null, RefusedUnprocessable(L["AgentProposalSuperseded"]));
        }

        if (proposal.Status == "committed" && proposal.CommittedRecordId.HasValue)
        {
          return (new AgentCommitResultDto
          {
            ProposalId = proposal.Id,
            RecordId = proposal.CommittedRecordId.Value,
            RecordLabel = proposal.CommittedLabel ?? $"#{proposal.CommittedRecordId.Value}",
            FormService = proposal.FormService ?? proposal.Service,
            RefreshServices = []
          }, null);
        }

        try
        {
          var committed = await AgentProposalExecutor.CommitProposalAsync(proposal, httpContext.RequestServices, innerCt);
          return (committed, null);
        }
        catch (InvalidOperationException ex) when (AgentRefusal.IsDomainRefusal(ex))
        {
          return (null, RefusedUnprocessable(ex.Message));
        }
      },
      ct);
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(AgentBatchCommitResultDto))]
  public Task<IResult> ConfirmBatch(
    AgentBatchConfirmRequest request,
    HttpContext httpContext,
    CancellationToken ct = default)
  {
    var proposalIds = request?.ProposalIds ?? [];
    var fingerprint = JsonSerializer.Serialize(proposalIds.Select(static id => id?.Trim() ?? string.Empty));
    var owner = AgentStore.ResolveOwner(httpContext.RequestServices);

    return RunIdempotentAsync<AgentBatchCommitResultDto>(
      httpContext,
      owner,
      "agent/confirm-batch",
      fingerprint,
      async innerCt =>
      {
        try
        {
          var committed = await AgentProposalExecutor.CommitBatchAsync(proposalIds, httpContext.RequestServices, innerCt);
          return (committed, null);
        }
        catch (InvalidOperationException ex) when (AgentRefusal.IsDomainRefusal(ex))
        {
          return (null, RefusedUnprocessable(ex.Message));
        }
      },
      ct);
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(AgentSavedMemoryDto))]
  public Task<IResult> ConfirmMemory(
    AgentMemoryConfirmRequest request,
    HttpContext httpContext,
    CancellationToken ct = default)
  {
    var token = request?.PendingToken?.Trim() ?? string.Empty;
    var owner = AgentStore.ResolveOwner(httpContext.RequestServices);

    return RunIdempotentAsync<AgentSavedMemoryDto>(
      httpContext,
      owner,
      "agent/confirm-memory",
      token,
      async innerCt =>
      {
        var saved = await AgentMemoryService.ConfirmInlineMemoryAsync(token, owner, httpContext.RequestServices, innerCt);
        return saved is not null
          ? (saved, null)
          : (null, RefusedNotFound(L["FormNotFound"]));
      },
      ct);
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(MutationResultDto))]
  public Task<IResult> UndoMemory(
    AgentMemoryUndoRequest request,
    HttpContext httpContext,
    CancellationToken ct = default)
  {
    var memoryId = request?.Id ?? 0;
    var owner = AgentStore.ResolveOwner(httpContext.RequestServices);

    return RunIdempotentAsync<MutationResultDto>(
      httpContext,
      owner,
      "agent/undo-memory",
      memoryId.ToString(CultureInfo.InvariantCulture),
      async innerCt =>
      {
        var deleted = await AgentMemoryService.UndoPersonalMemoryAsync(memoryId, owner, httpContext.RequestServices, innerCt);
        return deleted
          ? (new MutationResultDto { Outcome = MutationOutcome.Committed }, null)
          : (null, RefusedNotFound(L["FormNotFound"]));
      },
      ct);
  }

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(AgentAttachmentDto))]
  public async Task<IResult> Upload(
    IFormFileCollection files,
    HttpContext httpContext,
    [FromServices] AgentStore store,
    CancellationToken ct = default)
  {
    var file = files.GetFile("file") ?? files.FirstOrDefault();
    if (file is null || file.Length == 0)
    {
      return RefusedBadRequest(L["FileCountRefused"]);
    }

    var owner = AgentStore.ResolveOwner(httpContext.RequestServices);
    try
    {
      await using var stream = file.OpenReadStream();
      var saved = await store.SaveAttachmentAsync(owner, file.FileName, file.ContentType, stream, ct);
      return Results.Ok(saved);
    }
    catch (InvalidOperationException ex) when (AgentRefusal.IsDomainRefusal(ex))
    {
      return RefusedBadRequest(ex.Message);
    }
  }

  private static IResult RefusedBadRequest(string message) =>
    Results.BadRequest(new MutationResultDto
    {
      Outcome = MutationOutcome.Refused,
      Title = message,
      Messages = [new MessageDto { Detail = message, Type = MessageType.Error }]
    });

  private static IResult RefusedNotFound(string message) =>
    Results.NotFound(new MutationResultDto
    {
      Outcome = MutationOutcome.Refused,
      Title = message,
      Messages = [new MessageDto { Detail = message, Code = "refused.notFound", Type = MessageType.Error }]
    });

  private static IResult RefusedUnprocessable(string message) =>
    Results.UnprocessableEntity(new MutationResultDto
    {
      Outcome = MutationOutcome.Refused,
      Title = message,
      Messages = [new MessageDto { Detail = message, Type = MessageType.Error }]
    });

  private static async Task<IResult> RunIdempotentAsync<TDto>(
    HttpContext httpContext,
    AgentOwnerScope owner,
    string serviceKey,
    string payloadFingerprint,
    Func<CancellationToken, Task<(TDto? Committed, IResult? Failure)>> executeAsync,
    CancellationToken ct)
    where TDto : class
  {
    if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var idemVals) ||
        string.IsNullOrWhiteSpace(idemVals))
    {
      var (directDto, directFailure) = await executeAsync(ct);
      return directFailure ?? Results.Ok(directDto);
    }

    var store = httpContext.RequestServices.GetService<IdempotencyStore>() ?? IdempotencyStore.Default;
    var trimmedKey = idemVals.ToString().Trim();
    var payloadHash = IdempotencyStore.ComputePayloadHash(payloadFingerprint);

    using var keyLock = await store.AcquireLockAsync(owner.TenantId, owner.UserId, trimmedKey, ct);
    if (store.TryGetCommitted(owner.TenantId, owner.UserId, serviceKey, trimmedKey, payloadHash, out _, out var cached) &&
        cached is TDto replayed)
    {
      return Results.Ok(replayed);
    }

    var (committedDto, failure) = await executeAsync(ct);
    if (failure is not null || committedDto is null)
    {
      return failure!;
    }

    store.SaveCommitted(owner.TenantId, owner.UserId, serviceKey, trimmedKey, payloadHash, 0, committedDto);
    return Results.Ok(committedDto);
  }
}
