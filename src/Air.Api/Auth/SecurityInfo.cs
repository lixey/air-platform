namespace Platform.Api.Auth;

/// <summary>
/// What only the application knows about a user: the rights it holds, keys and patterns.
/// </summary>
public sealed record SecurityInfo(
  IReadOnlyList<string> Permissions,
  bool Global = false,
  long? HomeTenantId = null,
  IReadOnlySet<long>? AllowedTenantIds = null
)
{
  public bool CanAccessTenant(long tenantId) =>
    Global ||
    Permissions.Contains("*", StringComparer.OrdinalIgnoreCase) ||
    AllowedTenantIds != null && AllowedTenantIds.Contains(tenantId);
}
