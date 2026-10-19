using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Auth;

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
    (AllowedTenantIds != null && AllowedTenantIds.Contains(tenantId));
}

/// <summary>
/// What a user may do: the grants of the active roles of its active assignments, whose strings are the rights the checks
/// ask, and whether one of those roles is global, which opens every tenant to the user's scope.
/// </summary>
public static class GrantedRights
{
  public static async Task<SecurityInfo> LoadAsync<TAssignment, TRole, TGrant>(
    DbContext db,
    long userId,
    CancellationToken cancellationToken = default
  )
    where TAssignment : BaseAppAuthAssignment
    where TRole : BaseAppAuthRole
    where TGrant : BaseAppAuthGrant
  {
    ArgumentNullException.ThrowIfNull(db);

    var activeRoles = await (
      from a in db.Set<TAssignment>().AsNoTracking()
      join r in db.Set<TRole>().IgnoreQueryFilters().AsNoTracking() on a.AuthRoleId equals r.Id
      where a.UserId == userId && a.IsActive && r.IsActive
      select new { r.Id, r.TenantId }
    ).ToListAsync(cancellationToken);

    if (activeRoles.Count == 0)
    {
      return new SecurityInfo([]);
    }

    var roleIds = activeRoles.Select(r => r.Id).ToList();
    var isGlobal = activeRoles.Any(r => r.TenantId == null);
    var homeTenantId = activeRoles.FirstOrDefault(r => r.TenantId != null)?.TenantId;
    var allowedTenantIds = activeRoles
      .Where(r => r.TenantId != null)
      .Select(r => r.TenantId!.Value)
      .ToHashSet();

    var permissions = await db.Set<TGrant>()
      .AsNoTracking()
      .Where(g => roleIds.Contains(g.AuthRoleId))
      .Select(g => g.Permission)
      .Distinct()
      .ToListAsync(cancellationToken);

    return new SecurityInfo(permissions, isGlobal, homeTenantId, allowedTenantIds);
  }
}
