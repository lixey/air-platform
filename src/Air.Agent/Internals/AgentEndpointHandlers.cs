namespace Platform.Agent.Internals;

internal static class AgentEndpointHandlers
{
  internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public static bool HasDatabaseRegistered(IServiceProvider services) =>
    services.GetService<DbScope>() != null || services.GetService<PlatformDbContext>() != null;

  public static CultureInfo ApplyTurnCulture(HttpRequest request, string? locale)
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

  public static string? ResolveUserTimeZone(HttpContext ctx)
  {
    var userTz = ctx.RequestServices.GetService<IUserContext>()?.TimeZone;
    if (!string.IsNullOrWhiteSpace(userTz))
    {
      return userTz;
    }

    return ctx.Request.Headers.TryGetValue(AuthClaims.TimeZoneHeader, out var tzHeader) &&
           !string.IsNullOrWhiteSpace(tzHeader.ToString())
      ? tzHeader.ToString().Trim()
      : null;
  }

  public static string? ResolveLoopbackMcpUrl(HttpContext ctx)
  {
    var port = ctx.Connection.LocalPort;
    if (port <= 0 && ctx.Request.Host.Port is > 0 and var hostPort)
    {
      port = hostPort;
    }

    return port > 0 ? $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/api/mcp" : null;
  }

  public static async Task<(IAgentProvider? Provider, string SelectedModel, string SelectedModelName)> ResolveTurnProviderAndModelAsync(
    HttpContext ctx,
    AgentStore store,
    IAgentModelSynchronizer synchronizer,
    AgentOptions options,
    IEnumerable<IAgentProvider> providers,
    string? requestedProvider,
    string? requestedModel,
    bool preferPhotoModel,
    CancellationToken ct)
  {
    var enabledProviders = providers
      .Where(p => options.EnabledProviders.Count == 0 || options.EnabledProviders.Contains(p.Handle))
      .ToList();

    var providerHandle = !string.IsNullOrWhiteSpace(requestedProvider) ? requestedProvider : options.DefaultProvider;
    var selectedModel = requestedModel;
    string? selectedModelName = null;

    if (HasDatabaseRegistered(ctx.RequestServices))
    {
      await using var dbScope = AgentModuleScope.OpenDefault(ctx.RequestServices);
      var db = dbScope.Db;
      List<AppAgentModel> dbModels;
      (enabledProviders, dbModels) = await LoadActiveProvidersAndModelsAsync(
        db, store, synchronizer, options, enabledProviders, ct);

      AppAgentModel? PickProviderDefault() =>
        (!string.IsNullOrWhiteSpace(requestedProvider)
          ? dbModels.FirstOrDefault(m => m.IsDefault && string.Equals(m.ProviderHandle, requestedProvider, StringComparison.OrdinalIgnoreCase))
            ?? dbModels.FirstOrDefault(m => string.Equals(m.ProviderHandle, requestedProvider, StringComparison.OrdinalIgnoreCase))
          : null)
        ?? dbModels.FirstOrDefault(m => m.IsDefault)
        ?? dbModels.FirstOrDefault();

      if (!string.IsNullOrWhiteSpace(selectedModel))
      {
        var matchedRow = (!string.IsNullOrWhiteSpace(requestedProvider)
                           ? dbModels.FirstOrDefault(m =>
                             string.Equals(m.ModelId, selectedModel, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(m.ProviderHandle, requestedProvider, StringComparison.OrdinalIgnoreCase))
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
                           m.IsForPhotos && string.Equals(m.ProviderHandle, requestedProvider, StringComparison.OrdinalIgnoreCase))
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

  public static async Task<IResult> HandleStatusAsync(
    HttpContext ctx,
    AgentStore store,
    IEnumerable<IAgentProvider> providers,
    IEnumerable<IAgentSyncProvider> syncProviders,
    IAgentModelSynchronizer synchronizer,
    AgentOptions options,
    CancellationToken ct)
  {
    var enabledProviders = providers
      .Where(p => options.EnabledProviders.Count == 0 || options.EnabledProviders.Contains(p.Handle))
      .ToList();

    List<AgentModelDto> models = [];
    var defaultModel = options.DefaultModel;
    string? photoModel = null;

    if (HasDatabaseRegistered(ctx.RequestServices))
    {
      await using var dbScope = AgentModuleScope.OpenDefault(ctx.RequestServices);
      List<AppAgentModel> dbModels;
      (enabledProviders, dbModels) = await LoadActiveProvidersAndModelsAsync(
        dbScope.Db, store, synchronizer, options, enabledProviders, ct);

      var availableHandles = enabledProviders
        .Where(p => p.IsAvailable)
        .Select(p => p.Handle)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      if (availableHandles.Count > 0)
      {
        dbModels = dbModels.Where(m => availableHandles.Contains(m.ProviderHandle)).ToList();
      }

      var multiProvider = dbModels.Select(m => m.ProviderHandle).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
      models = dbModels.Select(m => new AgentModelDto
      {
        Id = m.ModelId,
        Name = multiProvider ? $"{m.Name} [{m.ProviderHandle}]" : m.Name,
        Provider = m.ProviderHandle,
        IsDefault = m.IsDefault,
        IsForPhotos = m.IsForPhotos
      }).ToList();

      var defRow = dbModels.FirstOrDefault(m => m.IsDefault) ?? dbModels.FirstOrDefault();
      defaultModel = defRow?.ModelId ?? options.DefaultModel;
      photoModel = (dbModels.FirstOrDefault(m => m.IsForPhotos) ?? defRow)?.ModelId;
    }

    var providerDtos = enabledProviders.Select(p => new AgentProviderDto
    {
      Handle = p.Handle,
      Name = p.Name,
      IsAvailable = p.IsAvailable
    }).ToList();

    var activeProvider = enabledProviders.FirstOrDefault(p =>
                           string.Equals(p.Handle, options.DefaultProvider, StringComparison.OrdinalIgnoreCase) && p.IsAvailable)
                         ?? enabledProviders.FirstOrDefault(p => p.IsAvailable)
                         ?? enabledProviders.FirstOrDefault();

    if (models.FirstOrDefault(m => m.IsDefault) is { } defModelDto)
    {
      activeProvider = enabledProviders.FirstOrDefault(p =>
        string.Equals(p.Handle, defModelDto.Provider, StringComparison.OrdinalIgnoreCase)) ?? activeProvider;
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
      catch { }
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

  public static async Task<IResult> HandleUploadAsync(HttpContext ctx, AgentStore store, CancellationToken ct)
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
      var saved = await store.SaveAttachmentAsync(owner, file.FileName, file.ContentType, stream, ct);
      return Results.Ok(saved);
    }
    catch (InvalidOperationException ex)
    {
      return Results.BadRequest(new { title = ex.Message });
    }
  }

  public static IResult HandleGetAttachment(string id, HttpContext ctx, AgentStore store)
  {
    var owner = AgentStore.ResolveOwner(ctx.RequestServices);
    var att = store.GetAttachment(id, owner);
    if (att is null || !File.Exists(att.LocalPath))
    {
      return Results.NotFound();
    }

    var safeMime = AgentAttachmentValidator.ClampStoredMime(att.ContentType);
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";

    if (AgentAttachmentValidator.IsInlineSafeImage(safeMime))
    {
      return Results.File(att.LocalPath, safeMime);
    }

    var safeDownloadName = AgentAttachmentValidator.SanitizeDownloadFileName(
      att.FileName,
      Path.GetExtension(att.LocalPath));
    return Results.File(att.LocalPath, safeMime, safeDownloadName);
  }

  public static async Task<IResult> HandleMemoryUndoAsync(
    AgentMemoryUndoRequest body,
    HttpContext ctx,
    CancellationToken ct)
  {
    var owner = AgentStore.ResolveOwner(ctx.RequestServices);
    var deleted = await AgentMemoryService.UndoPersonalMemoryAsync(body.Id, owner, ctx.RequestServices, ct);
    return deleted ? Results.Ok(new { ok = true, id = body.Id }) : Results.NotFound(new { ok = false });
  }

  public static async Task HandleChatAsync(
    AgentChatRequest body,
    HttpContext ctx,
    AgentStore store,
    AgentOptions options,
    McpExecutionBridge bridge,
    ITenantContext tenantContext,
    IEnumerable<IAgentProvider> providers,
    IAgentModelSynchronizer synchronizer,
    CancellationToken ct)
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

    var hasImageAttachment = attachments.Any(a => AgentAttachmentValidator.IsInlineSafeImage(a.ContentType));
    var (provider, selectedModel, selectedModelName) = await ResolveTurnProviderAndModelAsync(
      ctx, store, synchronizer, options, providers, body.Provider, body.Model, hasImageAttachment, ct);

    ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    if (provider is null)
    {
      var errJson = JsonSerializer.Serialize(
        new AgentStreamEventDto { Type = AgentStreamEventType.Error, Text = Localizer.Instance["AgentErrorCliMissing"] },
        JsonOptions);
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
      McpServerUrl = ResolveLoopbackMcpUrl(ctx),
      Services = ctx.RequestServices
    };

    var effectiveTimeZone = ResolveUserTimeZone(ctx);
    using var scope = bridge.BeginTurn(
      tenantContext.TenantId, tenantContext.UserId, tenantContext.Rights, sessionId, culture.Name, effectiveTimeZone);

    await foreach (var evt in provider.StreamTurnAsync(turnReq, ct))
    {
      var json = JsonSerializer.Serialize(evt, JsonOptions);
      await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
      await ctx.Response.Body.FlushAsync(ct);
    }
  }

  public static async Task SeedCatalogsInBackgroundAsync(IServiceProvider rootServices, CancellationToken ct)
  {
    try
    {
      using var _ = AsyncLocalTenantContext.UseSystemScope(correlationId: "agent-startup-seed");
      await using var scope = rootServices.CreateAsyncScope();
      if (!HasDatabaseRegistered(scope.ServiceProvider))
      {
        return;
      }

      await using var dbScope = AgentModuleScope.OpenDefault(scope.ServiceProvider);
      var opts = scope.ServiceProvider.GetService<AgentOptions>();
      var store = scope.ServiceProvider.GetService<AgentStore>();
      if (store is not null)
      {
        await store.EnsureCatalogsSeededAsync(dbScope.Db, opts?.DomainKnowledge, ct);
      }
      else
      {
        await AgentModelCatalog.EnsureSeededAsync(dbScope.Db, ct);
        await AgentMemoryCatalog.EnsureSeededAsync(dbScope.Db, opts?.DomainKnowledge, ct);
      }
    }
    catch
    {
      // Non-fatal if DB is not yet migrated on startup
    }
  }

  private static async Task<(List<IAgentProvider> EnabledProviders, List<AppAgentModel> DbModels)> LoadActiveProvidersAndModelsAsync(
    PlatformDbContext db,
    AgentStore store,
    IAgentModelSynchronizer synchronizer,
    AgentOptions options,
    List<IAgentProvider> enabledProviders,
    CancellationToken ct)
  {
    await store.EnsureCatalogsSeededAsync(db, options.DomainKnowledge, ct);
    var activeProviderHandles = await AgentModelCatalog.GetActiveProviderHandlesAsync(db, ct);
    var filteredProviders = enabledProviders.Where(p => activeProviderHandles.Contains(p.Handle)).ToList();
    var dbModels = await QueryActiveProviderModelsAsync(db, ct);

    if (dbModels.Count == 0 &&
        filteredProviders.Any(p => p.IsAvailable) &&
        store.TryClaimAutoSyncCooldown(TimeSpan.FromSeconds(60)))
    {
      try
      {
        await synchronizer.SyncFromProvidersAsync(db, ct);
        dbModels = await QueryActiveProviderModelsAsync(db, ct);
      }
      catch { }
    }

    return (filteredProviders, dbModels);
  }

  private static Task<List<AppAgentModel>> QueryActiveProviderModelsAsync(PlatformDbContext db, CancellationToken ct) =>
    db.AppAgentModel
      .AsNoTracking()
      .Where(m => m.IsActive && db.AppAgentProvider.Any(p => p.Handle == m.ProviderHandle && p.IsActive))
      .OrderBy(m => m.SortOrder)
      .ThenBy(m => m.Id)
      .ToListAsync(ct);
}
