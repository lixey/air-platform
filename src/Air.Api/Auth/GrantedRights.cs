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

    var isRoot = false;
    if ((db as PlatformDbContext)?.SetOfBase<BaseAppUser>() is { } usersSet)
    {
      isRoot = await usersSet.IgnoreQueryFilters().AsNoTracking()
        .Where(u => u.Id == userId)
        .Select(u => u.IsRoot)
        .FirstOrDefaultAsync(cancellationToken);
    }

    var activeRoles = await (
      from a in db.Set<TAssignment>().IgnoreQueryFilters().AsNoTracking()
      join r in db.Set<TRole>().IgnoreQueryFilters().AsNoTracking() on a.AuthRoleId equals r.Id
      where a.UserId == userId && a.IsActive && r.IsActive
      select new { r.Id, r.TenantId }
    ).ToListAsync(cancellationToken);

    if (activeRoles.Count == 0)
    {
      return isRoot ? new SecurityInfo(["*"], Global: true) : new SecurityInfo([]);
    }

    var isGlobal = isRoot || activeRoles.Any(r => r.TenantId == null);
    var homeTenantId = activeRoles.Where(r => r.TenantId != null).OrderBy(r => r.TenantId).FirstOrDefault()?.TenantId;
    var allowedTenantIds = activeRoles
      .Where(r => r.TenantId != null)
      .Select(r => r.TenantId!.Value)
      .ToHashSet();

    var effectiveTenantId = (db as PlatformDbContext)?.TenantContext.TenantId
      ?? AsyncLocalTenantContext.Instance.TenantId
      ?? homeTenantId;

    var roleIds = activeRoles
      .Where(r => effectiveTenantId == null || r.TenantId == null || r.TenantId == effectiveTenantId)
      .Select(r => r.Id)
      .ToList();

    if (roleIds.Count == 0)
    {
      return new SecurityInfo(isRoot ? ["*"] : [], isGlobal, homeTenantId, allowedTenantIds);
    }

    var permissions = await db.Set<TGrant>()
      .IgnoreQueryFilters()
      .AsNoTracking()
      .Where(g => roleIds.Contains(g.AuthRoleId))
      .Select(g => g.Permission)
      .Distinct()
      .ToListAsync(cancellationToken);

    if (isRoot && !permissions.Contains("*", StringComparer.OrdinalIgnoreCase))
    {
      permissions.Add("*");
    }

    return new SecurityInfo(permissions, isGlobal, homeTenantId, allowedTenantIds);
  }
}
