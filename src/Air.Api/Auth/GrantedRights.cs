namespace Platform.Api.Auth;

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

    var isGlobal = activeRoles.Any(r => r.TenantId == null);
    var homeTenantId = activeRoles.FirstOrDefault(r => r.TenantId != null)?.TenantId;
    var allowedTenantIds = activeRoles
      .Where(r => r.TenantId != null)
      .Select(r => r.TenantId!.Value)
      .ToHashSet();

    var effectiveTenantId = (db as Platform.Data.PlatformDbContext)?.TenantContext.TenantId
      ?? Platform.Data.Tenancy.AsyncLocalTenantContext.Instance.TenantId
      ?? homeTenantId;

    var roleIds = activeRoles
      .Where(r => effectiveTenantId == null || r.TenantId == null || r.TenantId == effectiveTenantId)
      .Select(r => r.Id)
      .ToList();

    if (roleIds.Count == 0)
    {
      return new SecurityInfo([], isGlobal, homeTenantId, allowedTenantIds);
    }

    var permissions = await db.Set<TGrant>()
      .AsNoTracking()
      .Where(g => roleIds.Contains(g.AuthRoleId))
      .Select(g => g.Permission)
      .Distinct()
      .ToListAsync(cancellationToken);

    return new SecurityInfo(permissions, isGlobal, homeTenantId, allowedTenantIds);
  }
}
