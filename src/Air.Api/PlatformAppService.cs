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

  /// <summary>Time provider for the current request or system default.</summary>
  public TimeProvider Time => Services.GetService<TimeProvider>() ?? TimeProvider.System;

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
  /// Whether the current principal may open the specified screen type.
  /// </summary>
  public bool CanOpen(Type screen)
  {
    var feat = Feature.FeaturesByServiceType.TryGetValue(screen, out var f) ? f : null;
    return feat == null || User.Can(feat.View);
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
