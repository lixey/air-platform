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
    services.AddPlatformService<PlatformAgentService>();
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
      .AddEndpointFilter(static async (ctx, next) =>
        ctx.HttpContext.User.Identity?.IsAuthenticated != true
          ? Results.Unauthorized()
          : await next(ctx));

    group.MapGet("/attachments/{id}", AgentEndpointHandlers.HandleGetAttachment);
    group.MapPost("/chat", AgentEndpointHandlers.HandleChatAsync);
    group.MapPost("/sync", AgentSyncEndpointHandler.HandleSyncAsync);
  }
}
