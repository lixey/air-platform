namespace Platform.Mcp;

public static class McpEndpointsExtensions
{
  public static IServiceCollection AddAirMcp(this IServiceCollection services)
  {
    services.TryAddSingleton<McpExecutionBridge>();
    return services;
  }

  public static IServiceCollection AddMcpTool<TTool>(this IServiceCollection services)
    where TTool : class, IMcpTool
  {
    services.AddAirMcp();
    services.TryAddEnumerable(ServiceDescriptor.Scoped<IMcpTool, TTool>());
    return services;
  }
}
