namespace Platform.Api.Auth;

/// <summary>
/// IUserContext read from the claims of the current request.
/// </summary>
public class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
  private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;
  private UserAuthSnapshot? Snapshot => httpContextAccessor.HttpContext?.Items[AuthClaims.SnapshotItem] as UserAuthSnapshot;

  private Dictionary<string, object?>? _customData;

  public bool IsAuthenticated =>
    Snapshot != null ||
    User?.Identity?.IsAuthenticated == true ||
    httpContextAccessor.HttpContext == null && AsyncLocalTenantContext.Instance.UserId != null;

  public long? UserId =>
    Snapshot?.Id ??
    Long(AuthClaims.Sub) ?? Long(ClaimTypes.NameIdentifier) ??
    (httpContextAccessor.HttpContext == null ? AsyncLocalTenantContext.Instance.UserId : null);

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

  public IReadOnlySet<string> Permissions =>
    field ??= Snapshot?.PermissionSet
      ?? (httpContextAccessor.HttpContext == null && AsyncLocalTenantContext.Instance.Rights is { } ambient
        ? ambient as IReadOnlySet<string> ?? ambient.ToHashSet(StringComparer.OrdinalIgnoreCase)
        : Values(AuthClaims.Permission));

  public string? Claim(string type) => User?.FindFirst(type)?.Value;

  public T? GetData<T>(string key)
  {
    if (string.IsNullOrWhiteSpace(key) || Snapshot?.CustomData is not { Count: > 0 } customData)
    {
      return default;
    }

    _customData ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    if (_customData.TryGetValue(key, out var cached))
    {
      return cached is T typed ? typed : default;
    }

    if (!customData.TryGetValue(key, out var rawElement))
    {
      var match = customData.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
      if (match.Key is null)
      {
        return default;
      }

      rawElement = match.Value;
    }

    if (rawElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
      return default;
    }

    var deserialized = rawElement.Deserialize<T>(PlatformJsonOptions.DefaultOptions);
    _customData[key] = deserialized;
    return deserialized;
  }

  public bool Can(string permission) =>
    IsAuthenticated && !string.IsNullOrWhiteSpace(permission) && HeldRights.Covers(Permissions, permission);

  private long? Long(string claim) => long.TryParse(User?.FindFirst(claim)?.Value, out var value) ? value : null;

  private HashSet<string> Values(string claim) =>
    (User?.FindAll(claim) ?? [])
    .Select(c => c.Value)
    .Where(v => !string.IsNullOrWhiteSpace(v))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
