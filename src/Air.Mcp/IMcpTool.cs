namespace Platform.Mcp;

/// <summary>
/// A tool exposed over the Model Context Protocol (MCP) and to in-process AI agent providers.
/// </summary>
public interface IMcpTool
{
  string Name { get; }
  string Description { get; }
  object InputSchema { get; }
  Task<object?> ExecuteAsync(JsonElement arguments, IServiceProvider services, CancellationToken ct);
}
