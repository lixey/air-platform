namespace Platform.Mcp;

/// <summary>
/// Holds the active user/tenant context for local CLI agent turns so loopback MCP calls run under the caller's session.
/// </summary>
public sealed class McpExecutionBridge
{
  private static readonly AsyncLocal<McpCallContext?> CurrentContext = new();
  private readonly ConcurrentDictionary<string, McpCallContext> _sessions = new(StringComparer.OrdinalIgnoreCase);

  public McpCallContext? Active => CurrentContext.Value;

  public McpCallContext? GetContext(string? sessionId) =>
    !string.IsNullOrWhiteSpace(sessionId)
      ? (_sessions.TryGetValue(sessionId, out var ctx) ? ctx : null)
      : CurrentContext.Value;

  public IDisposable BeginTurn(long? tenantId, long? userId, IReadOnlySet<string>? rights, string? sessionId)
  {
    var prev = CurrentContext.Value;
    var ctx = new McpCallContext(tenantId ?? 1, userId, rights, sessionId);
    CurrentContext.Value = ctx;
    if (!string.IsNullOrWhiteSpace(sessionId))
    {
      _sessions[sessionId] = ctx;
    }

    return new Scope(() =>
    {
      if (!string.IsNullOrWhiteSpace(sessionId))
      {
        _sessions.TryRemove(KeyValuePair.Create(sessionId, ctx));
      }

      if (ReferenceEquals(CurrentContext.Value, ctx))
      {
        CurrentContext.Value = prev is not null && (string.IsNullOrWhiteSpace(prev.SessionId) || _sessions.ContainsKey(prev.SessionId))
          ? prev
          : null;
      }
    });
  }

  private sealed class Scope(Action onDispose) : IDisposable
  {
    public void Dispose() => onDispose();
  }
}
