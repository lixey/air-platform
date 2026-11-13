using Platform.Agent.Internals;

namespace Platform.Agent;

public static class AgentEndpointsExtensions
{
  public static IServiceCollection AddPlatformAgent(this IServiceCollection services, Action<AgentOptions>? configure = null)
  {
    var options = new AgentOptions();
    configure?.Invoke(options);
    if (!string.IsNullOrWhiteSpace(options.DomainKnowledge))
    {
      AgentMemoryCatalog.DefaultDomainKnowledge = options.DomainKnowledge;
    }

    services.AddPlatformMcp();
    services.AddMcpTool<CatalogListMcpTool>();
    services.AddMcpTool<CatalogDescribeMcpTool>();
    services.AddMcpTool<RecordsQueryMcpTool>();
    services.AddMcpTool<ProposalStageMcpTool>();
    services.TryAddSingleton(options);
    services.TryAddSingleton<AgentStore>();
    services.AddSingleton<IAgentProvider, AgyCliAgentProvider>();
    services.AddSingleton<IAgentProvider, ClaudeCliAgentProvider>();
    services.AddSingleton<IAgentProvider, CodexCliAgentProvider>();
    services.AddSingleton<IAgentProvider, OpenAiCompatibleAgentProvider>();
    services.TryAddSingleton<IAgentModelSynchronizer, AgentModelSynchronizer>();
    return services;
  }

  public static PlatformOptions AddPlatformAgent(this PlatformOptions options, Action<AgentOptions>? configure = null)
  {
    ArgumentNullException.ThrowIfNull(options);
    options.AddContractAssembly(typeof(AgentEndpointsExtensions).Assembly);
    options.ConfigureBuilder(b => b.Services.AddPlatformAgent(configure));
    options.ConfigureApp(app => app.MapPlatformAgent());
    return options;
  }

  public static void MapPlatformAgent(this IEndpointRouteBuilder app)
  {
    app.MapPlatformMcp();

    if (app.ServiceProvider.GetService<IHostApplicationLifetime>() is { } lifetime)
    {
      lifetime.ApplicationStarted.Register(() =>
        _ = AgentEndpointHandlers.SeedCatalogsInBackgroundAsync(app.ServiceProvider, lifetime.ApplicationStopping));
    }

    var group = app.MapGroup("/api/agent")
      .AddEndpointFilter(async (ctx, next) =>
        ctx.HttpContext.User.Identity?.IsAuthenticated != true
          ? Results.Unauthorized()
          : await next(ctx));

    group.MapGet("/status", AgentEndpointHandlers.HandleStatusAsync);
    group.MapPost("/upload", AgentEndpointHandlers.HandleUploadAsync);
    group.MapGet("/attachments/{id}", AgentEndpointHandlers.HandleGetAttachment);
    group.MapPost("/memory-confirm", AgentMemoryEndpointHandlers.HandleMemoryConfirmAsync);
    group.MapPost("/memory-undo", AgentMemoryEndpointHandlers.HandleMemoryUndoAsync);

    group.MapPost("/reset", (AgentResetRequest req, HttpContext ctx, AgentStore store) =>
    {
      if (AgentStore.IsValidSessionId(req.SessionId))
      {
        store.ResetSession(req.SessionId!, AgentStore.ResolveOwner(ctx.RequestServices));
      }

      return Results.Ok(new { ok = true });
    });

    group.MapPost("/proposals/{id}/confirm", async (string id, HttpContext ctx, AgentStore store, CancellationToken ct) =>
    {
      var culture = PlatformEndpointsExtensions.ResolveRequestCulture(ctx.Request);
      CultureInfo.CurrentCulture = culture;
      CultureInfo.CurrentUICulture = culture;

      var owner = AgentStore.ResolveOwner(ctx.RequestServices);
      var proposal = store.GetVerifiedProposal(id, owner);
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
        return Results.Ok(await AgentProposalExecutor.CommitProposalAsync(proposal, ctx.RequestServices, ct));
      }
      catch (Exception ex)
      {
        return Results.UnprocessableEntity(new { title = ex.Message });
      }
    });

    group.MapPost("/proposals/confirm-batch", async (AgentBatchConfirmRequest body, HttpContext ctx, CancellationToken ct) =>
    {
      var culture = PlatformEndpointsExtensions.ResolveRequestCulture(ctx.Request);
      CultureInfo.CurrentCulture = culture;
      CultureInfo.CurrentUICulture = culture;

      try
      {
        return Results.Ok(await AgentProposalExecutor.CommitBatchAsync(body.ProposalIds, ctx.RequestServices, ct));
      }
      catch (Exception ex)
      {
        return Results.UnprocessableEntity(new { title = ex.Message });
      }
    });

    group.MapPost("/chat", AgentEndpointHandlers.HandleChatAsync);
    group.MapPost("/sync", AgentSyncEndpointHandler.HandleSyncAsync);
  }
}
