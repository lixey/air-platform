namespace Platform.Mcp;

/// <summary>
/// Holds the active user/tenant context for local CLI agent turns so loopback MCP calls run under the caller's session.
/// </summary>
public sealed class McpExecutionBridge
{
  private static readonly AsyncLocal<McpCallContext?> CurrentContext = new();
  private readonly ConcurrentDictionary<string, McpCallContext> _sessions = new(StringComparer.OrdinalIgnoreCase);

  public McpCallContext? Active => CurrentContext.Value;

  private static string BuildSessionKey(long? tenantId, long? userId, string sessionId) =>
    $"{tenantId ?? 1}:{userId ?? 0}:{sessionId}";

  public McpCallContext? GetContext(string? sessionId, long? tenantId = null, long? userId = null)
  {
    var active = CurrentContext.Value;
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      return active;
    }

    if (active is not null &&
        string.Equals(active.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) &&
        (!tenantId.HasValue || active.TenantId == tenantId.Value) &&
        (!userId.HasValue || active.UserId == userId.Value))
    {
      return active;
    }

    var effectiveTenantId = tenantId ?? active?.TenantId;
    var effectiveUserId = userId ?? active?.UserId;
    if (effectiveTenantId.HasValue &&
        _sessions.TryGetValue(BuildSessionKey(effectiveTenantId.Value, effectiveUserId, sessionId), out var scopedCtx))
    {
      return scopedCtx;
    }

    McpCallContext? matched = null;
    foreach (var (_, candidate) in _sessions)
    {
      if (!string.Equals(candidate.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (matched is not null)
      {
        return null;
      }

      matched = candidate;
    }

    return matched;
  }

  public IDisposable BeginTurn(long? tenantId, long? userId, IReadOnlySet<string>? rights, string? sessionId)
  {
    var prev = CurrentContext.Value;
    var resolvedTenantId = tenantId ?? 1;
    var ctx = new McpCallContext(resolvedTenantId, userId, rights, sessionId);
    CurrentContext.Value = ctx;
    var sessionKey = !string.IsNullOrWhiteSpace(sessionId)
      ? BuildSessionKey(resolvedTenantId, userId, sessionId)
      : null;
    if (sessionKey is not null)
    {
      _sessions[sessionKey] = ctx;
    }

    return new Scope(() =>
    {
      if (sessionKey is not null)
      {
        _sessions.TryRemove(KeyValuePair.Create(sessionKey, ctx));
      }

      if (ReferenceEquals(CurrentContext.Value, ctx))
      {
        CurrentContext.Value = prev is not null &&
          (string.IsNullOrWhiteSpace(prev.SessionId) ||
           _sessions.ContainsKey(BuildSessionKey(prev.TenantId, prev.UserId, prev.SessionId)))
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
