namespace Platform.Api.Auth;

/// <summary>
/// IUserContext read from the claims of the current request.
/// </summary>
public class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
  private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;
  private UserAuthSnapshot? Snapshot => httpContextAccessor.HttpContext?.Items[AuthClaims.SnapshotItem] as UserAuthSnapshot;

  public bool IsAuthenticated =>
    Snapshot != null ||
    User?.Identity?.IsAuthenticated == true ||
    AsyncLocalTenantContext.Instance.UserId != null;

  public long? UserId =>
    AsyncLocalTenantContext.Instance.UserId ??
    Snapshot?.Id ??
    Long(AuthClaims.Sub) ?? Long(ClaimTypes.NameIdentifier);

  public long? SessionId => Long(AuthClaims.SessionId);

  public string? Login =>
    Snapshot?.Login ??
    User?.FindFirst(AuthClaims.Login)?.Value ??
    User?.FindFirst(ClaimTypes.Name)?.Value;

  public int TokenVersion =>
    Snapshot?.TokenVersion ??
    (int.TryParse(User?.FindFirst(AuthClaims.Version)?.Value, out var version) ? version : 1);

  public bool MustChangePassword =>
    Snapshot?.MustChangePassword ??
    User?.HasClaim(AuthClaims.MustChangePassword, "true") == true;

  public string Language => CultureInfo.CurrentUICulture.Name;

  public string TimeZone =>
    Snapshot?.TimeZone is { Length: > 0 } tz
      ? tz
      : (httpContextAccessor.HttpContext?.Request.Headers[AuthClaims.TimeZoneHeader].FirstOrDefault() ?? "UTC");

  public IReadOnlySet<string> Permissions =>
    AsyncLocalTenantContext.Instance.Rights is { } ambient
      ? ambient
      : Snapshot?.PermissionSet ?? (field ??= Values(AuthClaims.Permission));

  public string? Claim(string type) => User?.FindFirst(type)?.Value;

  public bool Can(string permission) =>
    IsAuthenticated && !string.IsNullOrWhiteSpace(permission) && HeldRights.Covers(Permissions, permission);

  private long? Long(string claim) => long.TryParse(User?.FindFirst(claim)?.Value, out var value) ? value : null;

  private HashSet<string> Values(string claim) =>
    (User?.FindAll(claim) ?? [])
    .Select(c => c.Value)
    .Where(v => !string.IsNullOrWhiteSpace(v))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
