using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Auth;

/// <summary>
/// What only the application knows about a user: the rights it holds, keys and patterns.
/// </summary>
public sealed record SecurityInfo(IReadOnlyList<string> Permissions, bool Global = false, long? HomeTenantId = null);

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

    var assignments = await db.Set<TAssignment>()
      .Where(a => a.UserId == userId && a.IsActive)
      .Select(a => a.AuthRoleId)
      .ToListAsync(cancellationToken);

    if (assignments.Count == 0)
    {
      return new SecurityInfo([]);
    }

    var roles = await db.Set<TRole>()
      .IgnoreQueryFilters()
      .Where(r => assignments.Contains(r.Id) && r.IsActive)
      .ToListAsync(cancellationToken);

    var roleIds = roles.Select(r => r.Id).ToList();
    var isGlobal = roles.Any(r => r.TenantId == null);

    var permissions = await db.Set<TGrant>()
      .Where(g => roleIds.Contains(g.AuthRoleId))
      .Select(g => g.Permission)
      .Distinct()
      .ToListAsync(cancellationToken);

    return new SecurityInfo(permissions, isGlobal);
  }
}
