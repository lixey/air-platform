namespace Air.App;

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
}
