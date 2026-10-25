namespace Platform.Api.Auth.Contracts;

/// <summary>
/// Immutable cached snapshot of an authenticated user's profile, resolved rights, and custom session data for a tenant scope.
/// Never stores role names or role IDs: all authorization decisions use compiled permission keys.
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
  private FrozenSet<string>? _permissionSet;
  private FrozenSet<long>? _allowedTenants;

  [JsonIgnore]
  public IReadOnlySet<string> PermissionSet =>
    _permissionSet ??= Permissions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

  [JsonIgnore]
  public IReadOnlySet<long>? AllowedTenantSet =>
    AllowedTenantIds == null ? null : _allowedTenants ??= AllowedTenantIds.ToFrozenSet();

  public SecurityInfo ToSecurityInfo() =>
    new(Permissions, Global, HomeTenantId, AllowedTenantSet);
}
