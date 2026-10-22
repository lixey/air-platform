namespace Air.Mcp;

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

/// <summary>
/// Holds the active user/tenant context for local CLI agent turns so loopback MCP calls run under the caller's session.
/// </summary>
public sealed class McpExecutionBridge
{
  private volatile McpCallContext? _active;

  public McpCallContext? Active => _active;

  public IDisposable BeginTurn(long? tenantId, long? userId, IReadOnlySet<string>? rights, string? sessionId)
  {
    var prev = _active;
    _active = new McpCallContext(tenantId ?? 1, userId, rights, sessionId);
    return new Scope(() => _active = prev);
  }

  private sealed class Scope(Action onDispose) : IDisposable
  {
    public void Dispose() => onDispose();
  }
}

public sealed record McpCallContext(
  long? TenantId,
  long? UserId,
  IReadOnlySet<string>? Rights,
  string? SessionId);
