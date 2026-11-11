using Platform.Mcp.Internals;

namespace Platform.Mcp;

/// <summary>
/// Holds the active user/tenant context for local CLI agent turns so loopback MCP calls run under the caller's session.
/// </summary>
public sealed class McpExecutionBridge
{
  private static readonly AsyncLocal<McpCallContext?> CurrentContext = new();
  private readonly ConcurrentDictionary<string, McpCallContext> _sessions = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, McpCallContext> _turnTokens = new(StringComparer.Ordinal);

  public McpCallContext? Active => CurrentContext.Value;

  private static string BuildSessionKey(long? tenantId, long? userId, string sessionId) =>
    $"{(tenantId.HasValue ? tenantId.Value.ToString(CultureInfo.InvariantCulture) : "global")}:{(userId.HasValue ? userId.Value.ToString(CultureInfo.InvariantCulture) : "anon")}:{sessionId}";

  public McpCallContext? ResolveByTurnToken(string? turnToken)
  {
    if (string.IsNullOrWhiteSpace(turnToken))
    {
      return null;
    }

    var trimmed = turnToken.Trim();
    if (!_turnTokens.TryGetValue(trimmed, out var ctx) || string.IsNullOrEmpty(ctx.TurnToken))
    {
      return null;
    }

    var expectedBytes = Encoding.ASCII.GetBytes(ctx.TurnToken);
    var actualBytes = Encoding.ASCII.GetBytes(trimmed);
    return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes) ? ctx : null;
  }

  public McpCallContext? GetContext(string? sessionId, long? tenantId = null, long? userId = null)
  {
    var active = CurrentContext.Value;
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      if (active is not null &&
          (!tenantId.HasValue || active.TenantId == tenantId.Value) &&
          (!userId.HasValue || active.UserId == userId.Value))
      {
        return active;
      }

      return null;
    }

    if (active is not null &&
        string.Equals(active.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) &&
        (!tenantId.HasValue || active.TenantId == tenantId.Value) &&
        (!userId.HasValue || active.UserId == userId.Value))
    {
      return active;
    }

    if (!userId.HasValue)
    {
      return null;
    }

    var effectiveTenantId = tenantId ?? active?.TenantId;
    if (_sessions.TryGetValue(BuildSessionKey(effectiveTenantId, userId.Value, sessionId), out var scopedCtx) &&
        (!tenantId.HasValue || scopedCtx.TenantId == tenantId.Value) &&
        scopedCtx.UserId == userId.Value)
    {
      return scopedCtx;
    }

    return null;
  }

  public IDisposable UseTurnContext(McpCallContext context)
  {
    ArgumentNullException.ThrowIfNull(context);
    var prev = CurrentContext.Value;
    CurrentContext.Value = context;
    return new McpTurnScope(() =>
    {
      if (ReferenceEquals(CurrentContext.Value, context))
      {
        CurrentContext.Value = prev;
      }
    });
  }

  public IDisposable BeginTurn(long? tenantId, long? userId, IReadOnlySet<string>? rights, string? sessionId)
  {
    var prev = CurrentContext.Value;
    var turnToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    var ctx = new McpCallContext(tenantId, userId, rights, sessionId, turnToken);
    CurrentContext.Value = ctx;
    _turnTokens[turnToken] = ctx;

    var sessionKey = !string.IsNullOrWhiteSpace(sessionId)
      ? BuildSessionKey(tenantId, userId, sessionId)
      : null;
    if (sessionKey is not null)
    {
      _sessions[sessionKey] = ctx;
    }

    return new McpTurnScope(() =>
    {
      _turnTokens.TryRemove(KeyValuePair.Create(turnToken, ctx));

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
}
