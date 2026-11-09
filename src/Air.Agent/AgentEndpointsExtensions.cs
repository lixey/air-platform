namespace Platform.Agent;

public static class AgentEndpointsExtensions
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
  private static long _lastAutoSyncTicks;

  private static bool ShouldAttemptAutoSync()
  {
    var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
    var prev = Interlocked.Read(ref _lastAutoSyncTicks);
    if (nowTicks - prev < TimeSpan.FromSeconds(60).Ticks)
    {
      return false;
    }

    return Interlocked.CompareExchange(ref _lastAutoSyncTicks, nowTicks, prev) == prev;
  }

  private static async Task<(List<IAgentProvider> EnabledProviders, List<AppAgentModel> DbModels)> LoadActiveProvidersAndModelsAsync(
    PlatformDbContext db,
    AgentStore store,
    IAgentModelSynchronizer synchronizer,
    AgentOptions options,
    List<IAgentProvider> enabledProviders,
    CancellationToken ct
  )
  {
    await store.EnsureCatalogsSeededAsync(db, options.DomainKnowledge ?? AirAgentRuntime.SystemPromptSupplement, ct);

    var activeProviderHandles = await AgentModelCatalog.GetActiveProviderHandlesAsync(db, ct);
    var filteredProviders = enabledProviders
      .Where(p => activeProviderHandles.Contains(p.Handle))
      .ToList();

    var dbModels = await QueryActiveProviderModelsAsync(db, ct);
    if (dbModels.Count == 0 && filteredProviders.Any(p => p.IsAvailable) && ShouldAttemptAutoSync())
    {
      try
      {
        await synchronizer.SyncFromProvidersAsync(db, ct);
        dbModels = await QueryActiveProviderModelsAsync(db, ct);
      }
      catch
      {
        // Ignore transient sync errors
      }
    }

    return (filteredProviders, dbModels);
  }

  private static CultureInfo ApplyTurnCulture(HttpRequest request, string? locale)
  {
    var culture = !string.IsNullOrWhiteSpace(locale)
      ? (locale.StartsWith("uk", StringComparison.OrdinalIgnoreCase) ||
         locale.StartsWith("ua", StringComparison.OrdinalIgnoreCase)
        ? CultureInfo.GetCultureInfo("uk-UA")
        : locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)
          ? CultureInfo.GetCultureInfo("en-US")
          : PlatformEndpointsExtensions.ResolveRequestCulture(request))
      : PlatformEndpointsExtensions.ResolveRequestCulture(request);
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
    return culture;
  }

  private static async Task<(IAgentProvider? Provider, string SelectedModel, string SelectedModelName)> ResolveTurnProviderAndModelAsync(
    HttpContext ctx,
    AgentStore store,
    IAgentModelSynchronizer synchronizer,
    AgentOptions options,
    IEnumerable<IAgentProvider> providers,
    string? requestedProvider,
    string? requestedModel,
    bool preferPhotoModel,
    CancellationToken ct
  )
  {
    var enabledProviders = providers
      .Where(p => options.EnabledProviders.Count == 0 || options.EnabledProviders.Contains(p.Handle))
      .ToList();

    var providerHandle = !string.IsNullOrWhiteSpace(requestedProvider) ? requestedProvider : options.DefaultProvider;
    var selectedModel = requestedModel;
    string? selectedModelName = null;

    var db = ctx.RequestServices.GetService<PlatformDbContext>();
    if (db is not null)
    {
      List<AppAgentModel> dbModels;
      (enabledProviders, dbModels) = await LoadActiveProvidersAndModelsAsync(
        db,
        store,
        synchronizer,
        options,
        enabledProviders,
        ct
      );

      AppAgentModel? PickProviderDefault() =>
        (!string.IsNullOrWhiteSpace(requestedProvider)
          ? dbModels.FirstOrDefault(m =>
              m.IsDefault && string.Equals(m.ProviderHandle, requestedProvider, StringComparison.OrdinalIgnoreCase)
            )
            ?? dbModels.FirstOrDefault(m => string.Equals(m.ProviderHandle, requestedProvider, StringComparison.OrdinalIgnoreCase))
          : null)
        ?? dbModels.FirstOrDefault(m => m.IsDefault)
        ?? dbModels.FirstOrDefault();

      if (!string.IsNullOrWhiteSpace(selectedModel))
      {
        var matchedRow = (!string.IsNullOrWhiteSpace(requestedProvider)
                           ? dbModels.FirstOrDefault(m =>
                             string.Equals(m.ModelId, selectedModel, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(m.ProviderHandle, requestedProvider, StringComparison.OrdinalIgnoreCase)
                           )
                           : null)
                         ?? dbModels.FirstOrDefault(m => string.Equals(m.ModelId, selectedModel, StringComparison.OrdinalIgnoreCase))
                         ?? PickProviderDefault();
        if (matchedRow is not null)
        {
          selectedModel = matchedRow.ModelId;
          selectedModelName = matchedRow.Name;
          providerHandle = matchedRow.ProviderHandle;
        }
      }
      else if (preferPhotoModel)
      {
        var photoRow = (!string.IsNullOrWhiteSpace(requestedProvider)
                         ? dbModels.FirstOrDefault(m =>
                           m.IsForPhotos && string.Equals(m.ProviderHandle, requestedProvider, StringComparison.OrdinalIgnoreCase)
                         )
                         : null)
                       ?? PickProviderDefault() switch
                       {
                         var def when string.IsNullOrWhiteSpace(requestedProvider) => dbModels.FirstOrDefault(m => m.IsForPhotos) ?? def,
                         var def => def
                       };
        if (photoRow is not null)
        {
          selectedModel = photoRow.ModelId;
          selectedModelName = photoRow.Name;
          providerHandle = photoRow.ProviderHandle;
        }
      }
      else if (PickProviderDefault() is { } defRow)
      {
        selectedModel = defRow.ModelId;
        selectedModelName = defRow.Name;
        providerHandle = defRow.ProviderHandle;
      }
    }

    var provider = enabledProviders.FirstOrDefault(p => string.Equals(p.Handle, providerHandle, StringComparison.OrdinalIgnoreCase))
                   ?? enabledProviders.FirstOrDefault();

    var finalModel = !string.IsNullOrWhiteSpace(selectedModel) ? selectedModel : options.DefaultModel;
    var finalModelName = !string.IsNullOrWhiteSpace(selectedModelName) ? selectedModelName : finalModel;
    return (provider, finalModel, finalModelName);
  }

  private static Task<List<AppAgentModel>> QueryActiveProviderModelsAsync(PlatformDbContext db, CancellationToken ct) =>
    db.AppAgentModel
      .AsNoTracking()
      .Where(m => m.IsActive && db.AppAgentProvider.Any(p => p.Handle == m.ProviderHandle && p.IsActive))
      .OrderBy(m => m.SortOrder)
      .ThenBy(m => m.Id)
      .ToListAsync(ct);

  public static IServiceCollection AddAirAgent(this IServiceCollection services, Action<AgentOptions>? configure = null)
  {
    var options = new AgentOptions();
    configure?.Invoke(options);
    if (!string.IsNullOrWhiteSpace(options.DomainKnowledge))
    {
      AirAgentRuntime.SystemPromptSupplement = options.DomainKnowledge;
      AgentMemoryCatalog.DefaultDomainKnowledge = options.DomainKnowledge;
    }

    services.AddAirMcp();
    services.TryAddSingleton(options);
    services.TryAddSingleton<AgentStore>();
    services.AddSingleton<IAgentProvider, AgyCliAgentProvider>();
    services.AddSingleton<IAgentProvider, ClaudeCliAgentProvider>();
    services.AddSingleton<IAgentProvider, CodexCliAgentProvider>();
    services.AddSingleton<IAgentProvider, OpenAiCompatibleAgentProvider>();
    services.TryAddSingleton<IAgentModelSynchronizer, AgentModelSynchronizer>();
    return services;
  }

  public static PlatformOptions AddAirAgent(this PlatformOptions options, Action<AgentOptions>? configure = null)
  {
    ArgumentNullException.ThrowIfNull(options);
    options.ConfigureBuilder(b => b.Services.AddAirAgent(configure));
    options.ConfigureApp(app => app.MapAirAgent());
    return options;
  }

  public static void MapAirAgent(this IEndpointRouteBuilder app)
  {
    try
    {
      using var scope = app.ServiceProvider.CreateScope();
      var db = scope.ServiceProvider.GetService<PlatformDbContext>();
      if (db is not null)
      {
        var opts = scope.ServiceProvider.GetService<AgentOptions>();
        var store = scope.ServiceProvider.GetService<AgentStore>();
        if (store is not null)
        {
          store.EnsureCatalogsSeededAsync(db, opts?.DomainKnowledge ?? AirAgentRuntime.SystemPromptSupplement).GetAwaiter().GetResult();
        }
        else
        {
          AgentModelCatalog.EnsureSeededAsync(db).GetAwaiter().GetResult();
          AgentMemoryCatalog.EnsureSeededAsync(db, opts?.DomainKnowledge ?? AirAgentRuntime.SystemPromptSupplement).GetAwaiter()
            .GetResult();
        }

        if (!db.AppAgentModel.Any() &&
            scope.ServiceProvider.GetService<IAgentModelSynchronizer>() is { } synchronizer)
        {
          try
          {
            synchronizer.SyncFromProvidersAsync(db).GetAwaiter().GetResult();
          }
          catch
          {
            // Non-fatal if CLI provider sync fails on startup
          }
        }
      }
    }
    catch
    {
      // Non-fatal if DB is not yet migrated on startup
    }

    var group = app.MapGroup("/api/agent")
      .AddEndpointFilter(async (ctx, next) =>
        {
          if (ctx.HttpContext.User.Identity?.IsAuthenticated != true)
          {
            return Results.Unauthorized();
          }

          return await next(ctx);
        }
      );

    group.MapGet(
      "/status",
      async (
        HttpContext ctx,
        AgentStore store,
        IEnumerable<IAgentProvider> providers,
        IEnumerable<IAgentSyncProvider> syncProviders,
        IAgentModelSynchronizer synchronizer,
        AgentOptions options,
        CancellationToken ct
      ) =>
      {
        var enabledProviders = providers
          .Where(p => options.EnabledProviders.Count == 0 || options.EnabledProviders.Contains(p.Handle))
          .ToList();

        var db = ctx.RequestServices.GetService<PlatformDbContext>();
        List<AgentModelDto> models = [];
        var defaultModel = options.DefaultModel;
        string? photoModel = null;

        if (db is not null)
        {
          List<AppAgentModel> dbModels;
          (enabledProviders, dbModels) = await LoadActiveProvidersAndModelsAsync(
            db,
            store,
            synchronizer,
            options,
            enabledProviders,
            ct
          );

          var availableHandles = enabledProviders
            .Where(p => p.IsAvailable)
            .Select(p => p.Handle)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

          if (availableHandles.Count > 0)
          {
            dbModels = dbModels
              .Where(m => availableHandles.Contains(m.ProviderHandle))
              .ToList();
          }

          var multiProvider = dbModels.Select(m => m.ProviderHandle).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
          models = dbModels.Select(m => new AgentModelDto
          {
            Id = m.ModelId,
            Name = multiProvider ? $"{m.Name} [{m.ProviderHandle}]" : m.Name,
            Provider = m.ProviderHandle,
            IsDefault = m.IsDefault,
            IsForPhotos = m.IsForPhotos
          }
          ).ToList();

          var defRow = dbModels.FirstOrDefault(m => m.IsDefault) ?? dbModels.FirstOrDefault();
          defaultModel = defRow?.ModelId ?? options.DefaultModel;
          var photoRow = dbModels.FirstOrDefault(m => m.IsForPhotos) ?? defRow;
          photoModel = photoRow?.ModelId;
        }

        var list = enabledProviders.Select(p => new AgentProviderDto
        {
          Handle = p.Handle,
          Name = p.Name,
          IsAvailable = p.IsAvailable
        }
        ).ToList();

        var activeProvider = enabledProviders.FirstOrDefault(p =>
                               string.Equals(p.Handle, options.DefaultProvider, StringComparison.OrdinalIgnoreCase) && p.IsAvailable
                             )
                             ?? enabledProviders.FirstOrDefault(p => p.IsAvailable)
                             ?? enabledProviders.FirstOrDefault();

        if (models.FirstOrDefault(m => m.IsDefault) is { } defModelDto)
        {
          activeProvider = enabledProviders.FirstOrDefault(p => string.Equals(
                               p.Handle,
                               defModelDto.Provider,
                               StringComparison.OrdinalIgnoreCase
                             )
                           )
                           ?? activeProvider;
        }

        var syncSources = new List<AgentSyncSourceDto>();
        foreach (var sp in syncProviders)
        {
          try
          {
            var provided = await sp.GetSourcesAsync(ctx.RequestServices, ct);
            if (provided.Count > 0)
            {
              syncSources.AddRange(provided);
            }
          }
          catch
          {
            // Ignore sync source discovery errors in status endpoint
          }
        }

        return Results.Ok(
          new AgentStatusDto
          {
            ActiveProvider = activeProvider?.Handle ?? options.DefaultProvider,
            DefaultModel = defaultModel,
            PhotoModel = photoModel,
            Providers = list,
            Models = models,
            SyncSources = syncSources
          }
        );
      }
    );

    group.MapPost(
      "/upload",
      async (HttpContext ctx, AgentStore store, CancellationToken ct) =>
      {
        var req = ctx.Request;
        var culture = PlatformEndpointsExtensions.ResolveRequestCulture(req);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (!req.HasFormContentType)
        {
          return Results.BadRequest(new { title = Localizer.Instance["RequestInvalid"] });
        }

        var form = await req.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
          return Results.BadRequest(new { title = Localizer.Instance["FileCountRefused"] });
        }

        var owner = AgentStore.ResolveOwner(ctx.RequestServices);
        try
        {
          await using var stream = file.OpenReadStream();
          var saved = await store.SaveAttachmentAsync(
            owner,
            file.FileName,
            file.ContentType,
            stream,
            ct
          );
          return Results.Ok(saved);
        }
        catch (InvalidOperationException ex)
        {
          return Results.BadRequest(new { title = ex.Message });
        }
      }
    );

    group.MapGet(
      "/attachments/{id}",
      (string id, HttpContext ctx, AgentStore store) =>
      {
        var owner = AgentStore.ResolveOwner(ctx.RequestServices);
        var att = store.GetAttachment(id, owner);
        if (att is null || !File.Exists(att.LocalPath))
        {
          return Results.NotFound();
        }

        return Results.File(att.LocalPath, att.ContentType, att.FileName);
      }
    );

    group.MapPost(
      "/reset",
      (AgentResetRequest req, HttpContext ctx, AgentStore store) =>
      {
        if (AgentStore.IsValidSessionId(req.SessionId))
        {
          var owner = AgentStore.ResolveOwner(ctx.RequestServices);
          store.ResetSession(req.SessionId!, owner);
        }

        return Results.Ok(new { ok = true });
      }
    );

    group.MapPost(
      "/proposals/{id}/confirm",
      async (
        string id,
        HttpContext ctx,
        AgentStore store,
        CancellationToken ct
      ) =>
      {
        var culture = PlatformEndpointsExtensions.ResolveRequestCulture(ctx.Request);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        var owner = AgentStore.ResolveOwner(ctx.RequestServices);
        var proposal = store.GetProposal(id, owner);
        if (proposal is null)
        {
          return Results.NotFound(new { title = Localizer.Instance["AgentProposalNotFound"] });
        }

        if (proposal.Status == "superseded")
        {
          return Results.UnprocessableEntity(new { title = Localizer.Instance["AgentProposalSuperseded"] });
        }

        if (proposal.Status == "committed" && proposal.CommittedRecordId.HasValue)
        {
          return Results.Ok(
            new AgentCommitResultDto
            {
              ProposalId = proposal.Id,
              RecordId = proposal.CommittedRecordId.Value,
              RecordLabel = proposal.CommittedLabel ?? $"#{proposal.CommittedRecordId.Value}",
              FormService = proposal.FormService ?? proposal.Service,
              RefreshServices = []
            }
          );
        }

        try
        {
          var result = await AirAgentRuntime.CommitProposalAsync(proposal, ctx.RequestServices, ct);
          return Results.Ok(result);
        }
        catch (Exception ex)
        {
          return Results.UnprocessableEntity(
            new
            {
              title = ex.Message
            }
          );
        }
      }
    );

    group.MapPost(
      "/proposals/confirm-batch",
      async (
        AgentBatchConfirmRequest body,
        HttpContext ctx,
        CancellationToken ct
      ) =>
      {
        var culture = PlatformEndpointsExtensions.ResolveRequestCulture(ctx.Request);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        try
        {
          var result = await AirAgentRuntime.CommitBatchAsync(body.ProposalIds, ctx.RequestServices, ct);
          return Results.Ok(result);
        }
        catch (Exception ex)
        {
          return Results.UnprocessableEntity(
            new
            {
              title = ex.Message
            }
          );
        }
      }
    );

    group.MapPost(
      "/chat",
      async (
        AgentChatRequest body,
        HttpContext ctx,
        AgentStore store,
        AgentOptions options,
        McpExecutionBridge bridge,
        ITenantContext tenantContext,
        IEnumerable<IAgentProvider> providers,
        IAgentModelSynchronizer synchronizer,
        CancellationToken ct
      ) =>
      {
        var culture = ApplyTurnCulture(ctx.Request, body.Locale);

        if (!string.IsNullOrWhiteSpace(body.SessionId) && !AgentStore.IsValidSessionId(body.SessionId))
        {
          ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
          return;
        }

        var owner = AgentStore.ResolveOwner(ctx.RequestServices);
        var sessionId = AgentStore.NormalizeSessionId(body.SessionId);

        var attachments = (body.AttachmentIds ?? [])
          .Select(id => store.GetAttachment(id, owner))
          .Where(a => a is not null)
          .Cast<AgentAttachmentDto>()
          .ToList();

        var hasImageAttachment = attachments.Any(a =>
          string.IsNullOrEmpty(a.ContentType) || a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        );
        var (provider, selectedModel, selectedModelName) = await ResolveTurnProviderAndModelAsync(
          ctx,
          store,
          synchronizer,
          options,
          providers,
          body.Provider,
          body.Model,
          hasImageAttachment,
          ct
        );

        ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        if (provider is null)
        {
          var errJson = JsonSerializer.Serialize(
            new AgentStreamEventDto
            {
              Type = "error",
              Text = Localizer.Instance["AgentErrorCliMissing"]
            },
            JsonOptions
          );
          await ctx.Response.WriteAsync($"data: {errJson}\n\n", ct);
          await ctx.Response.Body.FlushAsync(ct);
          return;
        }

        var turnReq = new AgentTurnRequest
        {
          SessionId = sessionId,
          Owner = owner,
          Message = body.Message ?? string.Empty,
          Attachments = attachments,
          Locale = culture.TwoLetterISOLanguageName,
          ProviderHandle = provider.Handle,
          Model = selectedModel,
          ModelName = selectedModelName,
          Services = ctx.RequestServices
        };

        using var scope = bridge.BeginTurn(tenantContext.TenantId, tenantContext.UserId, tenantContext.Rights, sessionId);

        await foreach (var evt in provider.StreamTurnAsync(turnReq, ct))
        {
          var json = JsonSerializer.Serialize(evt, JsonOptions);
          await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
          await ctx.Response.Body.FlushAsync(ct);
        }
      }
    );

    group.MapPost(
      "/sync",
      async (
        AgentSyncRequest body,
        HttpContext ctx,
        AgentStore store,
        AgentOptions options,
        McpExecutionBridge bridge,
        ITenantContext tenantContext,
        IEnumerable<IAgentProvider> providers,
        IEnumerable<IAgentSyncProvider> syncProviders,
        IAgentModelSynchronizer synchronizer,
        CancellationToken ct
      ) =>
      {
        var culture = ApplyTurnCulture(ctx.Request, body.Locale);

        if (string.IsNullOrWhiteSpace(body.SourceId) ||
            !string.IsNullOrWhiteSpace(body.SessionId) && !AgentStore.IsValidSessionId(body.SessionId))
        {
          ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
          return;
        }

        IAgentSyncProvider? matchedSyncProvider = null;
        foreach (var sp in syncProviders)
        {
          try
          {
            var allowedSources = await sp.GetSourcesAsync(ctx.RequestServices, ct);
            if (allowedSources.Any(s => string.Equals(s.Id, body.SourceId, StringComparison.OrdinalIgnoreCase)))
            {
              matchedSyncProvider = sp;
              break;
            }
          }
          catch
          {
            // Ignore failing sync provider during source lookup
          }
        }

        if (matchedSyncProvider is null)
        {
          ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
          return;
        }

        var owner = AgentStore.ResolveOwner(ctx.RequestServices);
        var sessionId = AgentStore.NormalizeSessionId(body.SessionId);
        var attachments = (body.AttachmentIds ?? [])
          .Select(id => store.GetAttachment(id, owner))
          .Where(a => a is not null)
          .Cast<AgentAttachmentDto>()
          .ToList();

        ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        var writeLock = new SemaphoreSlim(1, 1);

        async Task WriteEventAsync(AgentStreamEventDto evt)
        {
          await writeLock.WaitAsync(ct);
          try
          {
            var json = JsonSerializer.Serialize(evt, JsonOptions);
            await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
          }
          finally
          {
            writeLock.Release();
          }
        }

        AgentSyncResult syncResult;
        try
        {
          syncResult = await matchedSyncProvider.ExecuteSyncAsync(
            new AgentSyncExecutionContext
            {
              SessionId = sessionId,
              SourceId = body.SourceId,
              Attachments = attachments,
              Locale = culture.TwoLetterISOLanguageName,
              Services = ctx.RequestServices,
              ReportStatusAsync = status => WriteEventAsync(new AgentStreamEventDto { Type = "status", Text = status })
            },
            ct
          );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          await WriteEventAsync(new AgentStreamEventDto { Type = "error", Text = ex.Message });
          return;
        }

        if (syncResult.RefreshServices.Count > 0)
        {
          await WriteEventAsync(
            new AgentStreamEventDto
            {
              Type = "refresh",
              RefreshServices = [.. syncResult.RefreshServices]
            }
          );
        }

        if (!syncResult.RequiresAgentTurn)
        {
          if (!string.IsNullOrWhiteSpace(syncResult.SummaryMarkdown))
          {
            await WriteEventAsync(new AgentStreamEventDto { Type = "delta", Text = syncResult.SummaryMarkdown });
          }

          await WriteEventAsync(new AgentStreamEventDto { Type = "done", SessionId = sessionId });
          return;
        }

        var (provider, selectedModel, selectedModelName) = await ResolveTurnProviderAndModelAsync(
          ctx,
          store,
          synchronizer,
          options,
          providers,
          body.Provider,
          body.Model,
          preferPhotoModel: false,
          ct
        );

        if (provider is null)
        {
          if (!string.IsNullOrWhiteSpace(syncResult.SummaryMarkdown))
          {
            await WriteEventAsync(new AgentStreamEventDto { Type = "delta", Text = syncResult.SummaryMarkdown });
          }

          await WriteEventAsync(new AgentStreamEventDto { Type = "error", Text = Localizer.Instance["AgentErrorCliMissing"] });
          return;
        }

        if (!string.IsNullOrWhiteSpace(syncResult.SummaryMarkdown))
        {
          await WriteEventAsync(
            new AgentStreamEventDto
            {
              Type = "delta",
              Text = $"{syncResult.SummaryMarkdown.TrimEnd()}\n\n"
            }
          );
        }

        var turnReq = new AgentTurnRequest
        {
          SessionId = sessionId,
          Owner = owner,
          Message = !string.IsNullOrWhiteSpace(syncResult.AgentPrompt)
            ? syncResult.AgentPrompt
            : "Review the synchronization context.",
          Locale = culture.TwoLetterISOLanguageName,
          ProviderHandle = provider.Handle,
          Model = selectedModel,
          ModelName = selectedModelName,
          SyncContext = syncResult.SyncContextJson,
          Services = ctx.RequestServices
        };

        using var scope = bridge.BeginTurn(tenantContext.TenantId, tenantContext.UserId, tenantContext.Rights, sessionId);

        await foreach (var evt in provider.StreamTurnAsync(turnReq, ct))
        {
          await WriteEventAsync(evt);
        }
      }
    );
  }
}
