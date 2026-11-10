using Platform.Api.Internals;

namespace Platform.Api;

/// <summary>
/// Aggregates ambient request services (user context, tenant context, localization and the request's
/// service provider), eliminating constructor parameter bloat across platform and application services.
/// </summary>
public class PlatformAppService(
  IUserContext user,
  IServiceProvider services,
  ITenantContext? tenant = null,
  ILocalizer? localizer = null
)
{
  /// <summary>The principal of the request: what it may see and do.</summary>
  public IUserContext User { get; } = user;

  /// <summary>The request's service provider.</summary>
  public IServiceProvider Services { get; } = services ?? throw new ArgumentNullException(nameof(services));

  /// <summary>The tenant the request runs in; the ambient one when the host names none.</summary>
  public ITenantContext Tenant { get; } = tenant ?? AsyncLocalTenantContext.Instance;

  /// <summary>The request's localizer.</summary>
  public ILocalizer L { get; } = localizer ?? Localizer.Instance;

  /// <summary>Password hasher and verifier.</summary>
  public IPasswordHasher Hasher => Services.GetService<IPasswordHasher>() ?? new PasswordHasher();

  /// <summary>Distributed user and session authentication cache.</summary>
  public IUserAuthCache? AuthCache => Services.GetService<IUserAuthCache>();

  /// <summary>Default UTC-scoped system time provider when no ambient request service is attached.</summary>
  public static TimeProvider UtcTime { get; } = new ScopedTimeProvider(TimeProvider.System, TimeZoneInfo.Utc);

  /// <summary>Time provider scoped to the active user/tenant time zone (or UTC when unconfigured).</summary>
  public TimeProvider Time => field ??= new ScopedTimeProvider(
    Services.GetService<TimeProvider>() ?? TimeProvider.System,
    TimeZones.TryFind(User.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc);

  /// <summary>Direct string translation indexer.</summary>
  public string this[string name] => L[name];

  /// <summary>Direct formatted string translation indexer.</summary>
  public string this[string name, params object[] arguments] => L[name, arguments];

  /// <summary>Whether the current principal holds the right.</summary>
  public bool Can(Right right) => User.Can(right);

  /// <summary>
  /// Whether the principal's held permissions cover every one of permissions.
  /// </summary>
  public bool Covers(IEnumerable<string> permissions)
  {
    ArgumentNullException.ThrowIfNull(permissions);
    var held = Tenant.Rights ?? User.Permissions;
    return permissions.All(p => HeldRights.Covers(held, p));
  }

  /// <summary>
  /// Whether the principal's held permissions cover every active role grant held by <paramref name="targetUserId"/>.
  /// </summary>
  public static async Task<bool> CoversUserGrantsAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext? user,
    long targetUserId,
    CancellationToken ct = default)
  {
    if (user?.UserId == targetUserId)
    {
      return true;
    }

    if (db.SetOfBase<BaseAppAuthGrant>() is not { } grantsSet ||
        db.SetOfBase<BaseAppAuthAssignment>() is not { } assignmentsSet ||
        db.SetOfBase<BaseAppAuthRole>() is not { } rolesSet)
    {
      return false;
    }

    var usersSet = db.SetOfBase<BaseAppUser>();
    var targetIsRoot = await IsRootUserAsync(usersSet, targetUserId, ct);
    var actorId = user?.UserId ?? db.TenantContext.UserId;
    var actorIsRoot = await IsRootUserAsync(usersSet, actorId, ct);
    if (targetIsRoot && !actorIsRoot)
    {
      return false;
    }

    var targetRoles = await (
      from r in rolesSet.IgnoreQueryFilters().AsNoTracking()
      join a in assignmentsSet.IgnoreQueryFilters().AsNoTracking() on r.Id equals a.AuthRoleId
      where a.UserId == targetUserId && a.IsActive && r.IsActive
      select new { r.Id, r.TenantId }
    ).ToListAsync(ct);

    if (!await ActorCoversRoleTenantsAsync(db, rolesSet, assignmentsSet, actorId, actorIsRoot, targetRoles.Select(r => r.TenantId), ct))
    {
      return false;
    }

    if (targetRoles.Count == 0)
    {
      return true;
    }

    var targetRoleIds = targetRoles.Select(r => r.Id).ToList();
    var grants = await grantsSet.IgnoreQueryFilters().AsNoTracking()
      .Where(g => targetRoleIds.Contains(g.AuthRoleId))
      .Select(g => g.Permission)
      .Distinct()
      .ToListAsync(ct);

    return CoversGrants(db, app, user, grants);
  }

  /// <summary>
  /// Whether the principal's held permissions and tenant scope cover the specified role.
  /// </summary>
  public static async Task<bool> CoversRoleGrantsAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext? user,
    long roleId,
    CancellationToken ct = default)
  {
    if (db.SetOfBase<BaseAppAuthGrant>() is not { } grantsSet ||
        db.SetOfBase<BaseAppAuthRole>() is not { } rolesSet)
    {
      return false;
    }

    var role = await rolesSet.IgnoreQueryFilters().AsNoTracking()
      .Where(r => r.Id == roleId)
      .Select(r => new { r.Id, r.TenantId })
      .FirstOrDefaultAsync(ct);
    if (role == null)
    {
      return true;
    }

    var actorId = user?.UserId ?? db.TenantContext.UserId;
    var actorIsRoot = await IsRootUserAsync(db.SetOfBase<BaseAppUser>(), actorId, ct);
    if (!await ActorCoversRoleTenantsAsync(db, rolesSet, db.SetOfBase<BaseAppAuthAssignment>(), actorId, actorIsRoot, [role.TenantId], ct))
    {
      return false;
    }

    var grants = await grantsSet.IgnoreQueryFilters().AsNoTracking()
      .Where(g => g.AuthRoleId == roleId)
      .Select(g => g.Permission)
      .Distinct()
      .ToListAsync(ct);

    return CoversGrants(db, app, user, grants);
  }

  private static async Task<bool> IsRootUserAsync(IQueryable<BaseAppUser>? usersSet, long? userId, CancellationToken ct) =>
    userId is > 0 && usersSet != null && await usersSet.IgnoreQueryFilters().AsNoTracking()
      .Where(u => u.Id == userId.Value)
      .Select(u => u.IsRoot)
      .FirstOrDefaultAsync(ct);

  private static async Task<bool> ActorCoversRoleTenantsAsync(
    PlatformDbContext db,
    IQueryable<BaseAppAuthRole> rolesSet,
    IQueryable<BaseAppAuthAssignment>? assignmentsSet,
    long? actorId,
    bool actorIsRoot,
    IEnumerable<long?> targetTenantIds,
    CancellationToken ct)
  {
    if (actorIsRoot || db.TenantContext.IsSystemScope)
    {
      return true;
    }

    var actorRoles = actorId is > 0 && assignmentsSet != null
      ? await (
          from r in rolesSet.IgnoreQueryFilters().AsNoTracking()
          join a in assignmentsSet.IgnoreQueryFilters().AsNoTracking() on r.Id equals a.AuthRoleId
          where a.UserId == actorId.Value && a.IsActive && r.IsActive
          select r.TenantId
        ).ToListAsync(ct)
      : [];
    if (actorRoles.Any(tid => tid == null))
    {
      return true;
    }

    var actorTenantSet = actorRoles.Where(tid => tid != null).Select(tid => tid!.Value).ToHashSet();
    if (db.TenantContext.TenantId is { } currentTid)
    {
      actorTenantSet.Add(currentTid);
    }

    return targetTenantIds.All(tid => tid != null && actorTenantSet.Contains(tid.Value));
  }

  private static bool CoversGrants(PlatformDbContext db, PlatformAppService? app, IUserContext? user, List<string> grants)
  {
    if (app != null)
    {
      return app.Covers(grants);
    }

    var held = db.TenantContext.Rights ?? user?.Permissions;
    return held != null && grants.All(p => HeldRights.Covers(held, p));
  }

  /// <summary>
  /// Revokes all active authentication sessions for the specified users.
  /// </summary>
  public static async Task RevokeUserSessionsAsync(
    PlatformDbContext db,
    IReadOnlyCollection<long> userIds,
    string reason,
    CancellationToken ct = default,
    PlatformAppService? app = null)
  {
    if (userIds.Count == 0 || db.SetOfBase<BaseAppAuthSession>() is not { } sessionsSet)
    {
      return;
    }

    var now = DateTimeOffset.UtcNow;
    var sessions = await sessionsSet
      .IgnoreQueryFilters()
      .Where(s => userIds.Contains(s.UserId) && s.RevokedAt == null)
      .ToListAsync(ct);
    foreach (var session in sessions)
    {
      session.RevokedAt = now;
      session.RevokedReason = reason;
    }

    if (app?.AuthCache is { } cache)
    {
      foreach (var session in sessions)
      {
        await cache.RemoveSessionAsync(session.Id, ct);
      }

      foreach (var uid in userIds)
      {
        await cache.RemoveUserAsync(uid, ct);
      }
    }
  }

  /// <summary>
  /// Whether the current principal may open the specified screen type.
  /// </summary>
  public bool CanOpen(Type screen)
  {
    var feat = Feature.FeaturesByServiceType.TryGetValue(screen, out var f)
      ? f
      : ModuleCatalog.Instance.FindFeature(ServicePathResolver.Resolve(screen));
    return feat == null || Host.ServiceEndpointsExtensions.CanView(feat, User);
  }

  /// <summary>
  /// Clears cached session/user auth snapshots when a security entity is mutated.
  /// </summary>
  public async Task ClearAuthCacheForEntityAsync(object? entity, PlatformDbContext? db = null, CancellationToken ct = default)
  {
    if (AuthCache is not { } cache)
    {
      return;
    }

    if (entity != null)
    {
      await ClearSingleAsync(cache, entity, ct);
    }

    if (db != null)
    {
      foreach (var entry in db.ChangeTracker.Entries<BaseAppAuthSession>())
      {
        if (entry.Entity.Id > 0)
        {
          await cache.RemoveSessionAsync(entry.Entity.Id, ct);
        }
      }
    }
  }

  /// <summary>
  /// Clears cached session/user auth snapshots when security entities are mutated in a batch.
  /// </summary>
  public async Task ClearAuthCacheForEntitiesAsync(IEnumerable<object> entities, PlatformDbContext? db = null, CancellationToken ct = default)
  {
    if (AuthCache is not { } cache)
    {
      return;
    }

    foreach (var entity in entities)
    {
      await ClearSingleAsync(cache, entity, ct);
    }

    if (db != null)
    {
      foreach (var entry in db.ChangeTracker.Entries<BaseAppAuthSession>())
      {
        if (entry.Entity.Id > 0)
        {
          await cache.RemoveSessionAsync(entry.Entity.Id, ct);
        }
      }
    }
  }

  private static async Task ClearSingleAsync(IUserAuthCache cache, object entity, CancellationToken ct)
  {
    switch (entity)
    {
      case BaseAppUser u:
        await cache.RemoveUserAsync(u.Id, ct);
        break;
      case BaseAppAuthSession s:
        await cache.RemoveSessionAsync(s.Id, ct);
        await cache.RemoveUserAsync(s.UserId, ct);
        break;
      case BaseAppAuthAssignment a:
        await cache.RemoveUserAsync(a.UserId, ct);
        break;
      case BaseAppAuthRole or BaseAppAuthGrant or BaseAppTenant:
        await cache.ClearAsync(ct);
        break;
    }
  }
}
