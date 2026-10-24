namespace Air.Agent;

public static class AgentEndpoints
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public static IServiceCollection AddAirAgent(this IServiceCollection services, Action<AgentOptions>? configure = null)
  {
    var options = new AgentOptions();
    configure?.Invoke(options);

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

  public static void MapAirAgent(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/agent");

    group.MapGet("/status", async (
      HttpContext ctx,
      IEnumerable<IAgentProvider> providers,
      AgentOptions options,
      CancellationToken ct) =>
    {
      var enabledProviders = providers
        .Where(p => options.EnabledProviders.Count == 0 || options.EnabledProviders.Contains(p.Handle))
        .ToList();

      var list = enabledProviders.Select(p => new AgentProviderDto
      {
        Handle = p.Handle,
        Name = p.Name,
        IsAvailable = p.IsAvailable
      }).ToList();

      var activeProvider = enabledProviders.FirstOrDefault(p => string.Equals(p.Handle, options.DefaultProvider, StringComparison.OrdinalIgnoreCase) && p.IsAvailable)
        ?? enabledProviders.FirstOrDefault(p => p.IsAvailable)
        ?? enabledProviders.FirstOrDefault();

      var db = ctx.RequestServices.GetService<PlatformDbContext>();
      List<AgentModelDto> models;
      string defaultModel;
      string? photoModel;

      if (db is not null)
      {
        await AgentModelCatalog.EnsureSeededAsync(db, ct);
        var dbModels = await db.AppAgentModel
          .AsNoTracking()
          .Where(m => m.IsActive)
          .OrderBy(m => m.SortOrder)
          .ThenBy(m => m.Id)
          .ToListAsync(ct);

        if (dbModels.Count == 0)
        {
          dbModels = [.. AgentModelCatalog.DefaultRows().Where(m => m.IsActive)];
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
        var photoRow = dbModels.FirstOrDefault(m => m.IsForPhotos)
          ?? dbModels.FirstOrDefault(m => string.Equals(m.ModelId, AgentModelCatalog.DefaultPhotoModelId, StringComparison.OrdinalIgnoreCase))
          ?? defRow;
        photoModel = photoRow?.ModelId ?? AgentModelCatalog.DefaultPhotoModelId;
        if (defRow is not null)
        {
          activeProvider = enabledProviders.FirstOrDefault(p => string.Equals(p.Handle, defRow.ProviderHandle, StringComparison.OrdinalIgnoreCase))
            ?? activeProvider;
        }
      }
      else
      {
        var fallback = AgentModelCatalog.DefaultRows().Where(m => m.IsActive);
        models = fallback.Select(m => new AgentModelDto
        {
          Id = m.ModelId,
          Name = m.Name,
          Provider = m.ProviderHandle,
          IsDefault = m.IsDefault,
          IsForPhotos = m.IsForPhotos
        }).ToList();
        defaultModel = AgentModelCatalog.DefaultModelId;
        photoModel = AgentModelCatalog.DefaultPhotoModelId;
      }

      return Results.Ok(new AgentStatusDto
      {
        ActiveProvider = activeProvider?.Handle ?? options.DefaultProvider,
        DefaultModel = defaultModel,
        PhotoModel = photoModel,
        Providers = list,
        Models = models
      });
    });

    group.MapPost("/upload", async (HttpContext ctx, AgentStore store, CancellationToken ct) =>
    {
      var req = ctx.Request;
      var culture = PlatformEndpoints.ResolveRequestCulture(req);
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
      await using var stream = file.OpenReadStream();
      var saved = await store.SaveAttachmentAsync(owner, file.FileName, file.ContentType, stream, ct);
      return Results.Ok(saved);
    });

    group.MapGet("/attachments/{id}", (string id, HttpContext ctx, AgentStore store) =>
    {
      var owner = AgentStore.ResolveOwner(ctx.RequestServices);
      var att = store.GetAttachment(id, owner);
      if (att is null || !File.Exists(att.LocalPath))
      {
        return Results.NotFound();
      }

      return Results.File(att.LocalPath, att.ContentType, att.FileName);
    });

    group.MapPost("/reset", (AgentResetRequestDto req, HttpContext ctx, AgentStore store) =>
    {
      if (!string.IsNullOrWhiteSpace(req.SessionId))
      {
        var owner = AgentStore.ResolveOwner(ctx.RequestServices);
        store.ResetSession(req.SessionId, owner);
      }

      return Results.Ok(new { ok = true });
    });

    group.MapPost("/proposals/{id}/confirm", async (
      string id,
      HttpContext ctx,
      AgentStore store,
      CancellationToken ct) =>
    {
      var culture = PlatformEndpoints.ResolveRequestCulture(ctx.Request);
      CultureInfo.CurrentCulture = culture;
      CultureInfo.CurrentUICulture = culture;

      var owner = AgentStore.ResolveOwner(ctx.RequestServices);
      var proposal = store.GetProposal(id, owner);
      if (proposal is null)
      {
        return Results.NotFound(new { title = Localizer.Instance["AgentProposalNotFound"] });
      }

      if (proposal.Status == "committed" && proposal.CommittedRecordId.HasValue)
      {
        return Results.Ok(new AgentCommitResultDto
        {
          ProposalId = proposal.Id,
          RecordId = proposal.CommittedRecordId.Value,
          RecordLabel = proposal.CommittedLabel ?? $"#{proposal.CommittedRecordId.Value}",
          FormService = proposal.FormService ?? proposal.Service,
          RefreshServices = []
        });
      }

      try
      {
        var result = await AirAgentRuntime.CommitProposalAsync(proposal, ctx.RequestServices, ct);
        return Results.Ok(result);
      }
      catch (Exception ex)
      {
        return Results.UnprocessableEntity(new
        {
          title = ex.Message
        });
      }
    });

    group.MapPost("/chat", async (
      AgentChatRequestDto body,
      HttpContext ctx,
      AgentStore store,
      AgentOptions options,
      McpExecutionBridge bridge,
      ITenantContext tenantContext,
      IEnumerable<IAgentProvider> providers,
      CancellationToken ct) =>
    {
      var culture = !string.IsNullOrWhiteSpace(body.Locale) && body.Locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)
        ? CultureInfo.GetCultureInfo("en-US")
        : PlatformEndpoints.ResolveRequestCulture(ctx.Request);
      CultureInfo.CurrentCulture = culture;
      CultureInfo.CurrentUICulture = culture;

      var owner = AgentStore.ResolveOwner(ctx.RequestServices);
      var sessionId = string.IsNullOrWhiteSpace(body.SessionId)
        ? Guid.NewGuid().ToString("N")[..12]
        : body.SessionId;

      var enabledProviders = providers
        .Where(p => options.EnabledProviders.Count == 0 || options.EnabledProviders.Contains(p.Handle))
        .ToList();

      var providerHandle = body.Provider ?? options.DefaultProvider;
      var provider = enabledProviders.FirstOrDefault(p => string.Equals(p.Handle, providerHandle, StringComparison.OrdinalIgnoreCase))
        ?? enabledProviders.FirstOrDefault()
        ?? providers.First();

      var attachments = (body.AttachmentIds ?? [])
        .Select(id => store.GetAttachment(id, owner))
        .Where(a => a is not null)
        .Cast<AgentAttachmentDto>()
        .ToList();

      var selectedModel = body.Model;
      string? selectedModelName = null;
      var db = ctx.RequestServices.GetService<PlatformDbContext>();
      if (db is not null)
      {
        await AgentModelCatalog.EnsureSeededAsync(db, ct);
        var dbModels = await db.AppAgentModel.AsNoTracking().ToListAsync(ct);
        if (attachments.Count > 0)
        {
          var photoRow = dbModels.FirstOrDefault(m => m.IsForPhotos && m.IsActive)
            ?? dbModels.FirstOrDefault(m => m.IsForPhotos)
            ?? dbModels.FirstOrDefault(m => string.Equals(m.ModelId, AgentModelCatalog.DefaultPhotoModelId, StringComparison.OrdinalIgnoreCase));
          if (photoRow is not null)
          {
            selectedModel = photoRow.ModelId;
            selectedModelName = photoRow.Name;
            provider = enabledProviders.FirstOrDefault(p => string.Equals(p.Handle, photoRow.ProviderHandle, StringComparison.OrdinalIgnoreCase))
              ?? provider;
          }
        }
        else if (!string.IsNullOrWhiteSpace(selectedModel))
        {
          var matchedRow = dbModels.FirstOrDefault(m => string.Equals(m.ModelId, selectedModel, StringComparison.OrdinalIgnoreCase));
          if (matchedRow is not null)
          {
            selectedModelName = matchedRow.Name;
          }
        }
        else
        {
          var defRow = dbModels.FirstOrDefault(m => m.IsDefault && m.IsActive) ?? dbModels.FirstOrDefault(m => m.IsActive);
          if (defRow is not null)
          {
            selectedModel = defRow.ModelId;
            selectedModelName = defRow.Name;
          }
        }
      }

      if (string.IsNullOrWhiteSpace(selectedModelName))
      {
        var effectiveId = !string.IsNullOrWhiteSpace(selectedModel)
          ? selectedModel
          : (attachments.Count > 0 ? AgentModelCatalog.DefaultPhotoModelId : options.DefaultModel);
        selectedModel = effectiveId;
        selectedModelName = AgentModelCatalog.DefaultRows()
          .FirstOrDefault(m => string.Equals(m.ModelId, effectiveId, StringComparison.OrdinalIgnoreCase))?.Name
          ?? effectiveId;
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

      ctx.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
      ctx.Response.Headers.CacheControl = "no-cache";
      ctx.Response.Headers.Connection = "keep-alive";

      using var scope = bridge.BeginTurn(tenantContext.TenantId, tenantContext.UserId, tenantContext.Rights, sessionId);

      await foreach (var evt in provider.StreamTurnAsync(turnReq, ct))
      {
        var json = JsonSerializer.Serialize(evt, JsonOptions);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
      }
    });
  }
}
