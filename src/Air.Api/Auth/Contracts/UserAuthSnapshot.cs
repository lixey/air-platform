namespace Platform.Api.Auth.Contracts;

/// <summary>
/// Immutable cached snapshot of an authenticated user's profile, permissions, and enriched context data for a tenant scope.
/// </summary>
public sealed record UserAuthSnapshot(
  long Id,
  string Login,
  string Name,
  string Email,
  string TimeZone,
  bool IsActive,
  bool IsRoot,
  int TokenVersion,
  bool MustChangePassword,
  long? HomeTenantId,
  IReadOnlyList<string> Permissions,
  bool Global = false,
  IReadOnlyList<long>? AllowedTenantIds = null,
  Dictionary<string, JsonElement>? CustomData = null
)
{
  private HashSet<string>? _permissionSet;
  private HashSet<long>? _allowedTenants;

  [JsonIgnore]
  public IReadOnlySet<string> PermissionSet =>
    _permissionSet ??= Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

  [JsonIgnore]
  public IReadOnlySet<long>? AllowedTenantSet =>
    AllowedTenantIds == null ? null : _allowedTenants ??= AllowedTenantIds.ToHashSet();

  public SecurityInfo ToSecurityInfo() =>
    new(Permissions, Global, HomeTenantId, AllowedTenantSet);
}
