using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Platform.Data.Internals;

namespace Platform.Data;

/// <summary>
/// Base EF Core DbContext for the platform and its modules.
/// Automates tenant isolation (IHasTenant, IHasOptionalTenant), audit timestamps (IHasTimestamps),
/// and optimistic concurrency versioning (IHasVersion).
/// </summary>
public abstract partial class PlatformDbContext : DbContext
{
  private readonly DbContextOptions? _options;

  protected PlatformDbContext()
  {
  }

  protected PlatformDbContext(DbContextOptions options) : base(options)
  {
    _options = options;
  }

  protected PlatformDbContext(DbContextOptions options, ITenantContext? tenantContext) : base(options)
  {
    _options = options;
    TenantContext = tenantContext;
  }

  [AllowNull]
  public ITenantContext TenantContext => field ?? AsyncLocalTenantContext.Instance;

  public TimeProvider Time
  {
    get => field ??= ResolveTimeProvider();
    init;
  }

  public string? ModuleHandle => ModuleOwnershipExtensions.GetDeclaredModuleHandle(GetType());

  public DbSet<AppLanguage> AppLanguage => Set<AppLanguage>();
  public DbSet<AppAgentProvider> AppAgentProvider => Set<AppAgentProvider>();
  public DbSet<AppAgentModel> AppAgentModel => Set<AppAgentModel>();
  public DbSet<AppAgentMemory> AppAgentMemory => Set<AppAgentMemory>();
  public DbSet<AppPlatformInstallation> AppPlatformInstallation => Set<AppPlatformInstallation>();
  public DbSet<AppPlatformChange> AppPlatformChange => Set<AppPlatformChange>();

  public long RequiredTenantId => TenantContext.TenantId
                                  ?? throw new InvalidOperationException("Operation requires an active tenant, but none was resolved.");

  private static readonly ConcurrentDictionary<(Type ContextType, Type BaseType), Func<PlatformDbContext, object?>?> BaseSetAccessors =
    new();

  public IQueryable<TBase>? SetOfBase<TBase>() where TBase : class
  {
    var accessor = BaseSetAccessors.GetOrAdd(
      (GetType(), typeof(TBase)),
      key =>
      {
        var clrType = Model.GetEntityTypes()
          .Select(t => t.ClrType)
          .FirstOrDefault(t => !t.IsAbstract && key.BaseType.IsAssignableFrom(t));
        if (clrType is null)
        {
          return null;
        }

        var ctxParam = Expression.Parameter(typeof(PlatformDbContext), "ctx");
        var call = Expression.Call(ctxParam, nameof(Set), [clrType]);
        return Expression.Lambda<Func<PlatformDbContext, object?>>(call, ctxParam).Compile();
      });
    return (IQueryable<TBase>?)accessor?.Invoke(this);
  }

  public async Task<Dictionary<long, string>> LoadUserNamesAsync(
    IReadOnlyCollection<long> userIds,
    CancellationToken cancellationToken = default)
  {
    var result = new Dictionary<long, string>();
    if (userIds.Count == 0 || SetOfBase<BaseAppUser>() is not { } userQuery)
    {
      return result;
    }

    var rows = await userQuery
      .IgnoreTenantFilter()
      .AsNoTracking()
      .Where(u => userIds.Contains(u.Id))
      .Select(u => new { u.Id, u.Name, u.Login })
      .ToListAsync(cancellationToken);

    foreach (var r in rows)
    {
      result[r.Id] = !string.IsNullOrWhiteSpace(r.Name) ? r.Name : r.Login;
    }

    return result;
  }

  protected sealed override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
    PlatformMigrationHistory.Apply(optionsBuilder, ModuleHandle, GetType());

  protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
  {
    base.ConfigureConventions(configurationBuilder);
    configurationBuilder.Conventions.Add(_ => new PlatformModelConvention(this));
    configurationBuilder.Conventions.Add(_ => new ModuleOwnershipConvention(GetType()));
    configurationBuilder.Properties<DateTime>().HaveConversion<LocalDateTimeConverter>();
    configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcOffsetConverter>();
  }

  private TimeProvider ResolveTimeProvider() =>
    _options?.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?.GetService<TimeProvider>()
    ?? TimeProvider.System;
}
