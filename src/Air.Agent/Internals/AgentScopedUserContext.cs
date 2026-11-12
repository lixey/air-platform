namespace Platform.Agent.Internals;

internal sealed class AgentScopedUserContext(
  IUserContext? inner,
  string? localeOverride,
  string? timeZoneOverride,
  long? fallbackUserId = null,
  IReadOnlySet<string>? fallbackPermissions = null
) : IUserContext
{
  private static readonly HashSet<string> EmptyPermissions = [];

  public long? UserId => inner?.UserId ?? fallbackUserId ?? AsyncLocalTenantContext.Instance.UserId;
  public long? SessionId => inner?.SessionId;
  public string? Login => inner?.Login;
  public int TokenVersion => inner?.TokenVersion ?? 1;
  public bool IsAuthenticated => inner?.IsAuthenticated == true || UserId is > 0;
  public bool MustChangePassword => inner?.MustChangePassword ?? false;

  public string Language =>
    !string.IsNullOrWhiteSpace(localeOverride)
      ? localeOverride
      : inner?.Language is { Length: > 0 } lang
        ? lang
        : CultureInfo.CurrentUICulture.Name;

  public string TimeZone =>
    !string.IsNullOrWhiteSpace(timeZoneOverride)
      ? timeZoneOverride
      : inner?.TimeZone is { Length: > 0 } tz
        ? tz
        : "UTC";

  public IReadOnlySet<string> Permissions =>
    AsyncLocalTenantContext.Instance.Rights
    ?? fallbackPermissions
    ?? inner?.Permissions
    ?? EmptyPermissions;

  public bool Can(string permission) =>
    IsAuthenticated && !string.IsNullOrWhiteSpace(permission) && HeldRights.Covers(Permissions, permission);

  public string? Claim(string type) => inner?.Claim(type);

  public static IUserContext Wrap(IUserContext? inner, McpCallContext? mcpContext)
  {
    if (mcpContext == null && inner != null)
    {
      return inner;
    }

    return new AgentScopedUserContext(
      inner,
      mcpContext?.Locale,
      mcpContext?.TimeZone,
      mcpContext?.UserId,
      mcpContext?.Rights);
  }
}
