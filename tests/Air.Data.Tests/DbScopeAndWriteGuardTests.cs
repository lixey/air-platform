using Microsoft.Extensions.DependencyInjection;
using Platform.Data.Tests.Internals;

namespace Platform.Data.Tests;

public sealed class DbScopeAndWriteGuardTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public DbScopeAndWriteGuardTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
  }

  public void Dispose() => _connection.Dispose();

  private TestDbContext CreateDbContext(ITenantContext tenant)
  {
    var options = new DbContextOptionsBuilder<TestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var db = new TestDbContext(options, tenant);
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public async Task DbScope_SupportsParallelLeafModules_ReverseRegistration_AndGuardsConflictingRebind()
  {
    var services = new ServiceCollection();
    // Register leaf BEFORE base and register duplicate leaf to verify zero recursion / stack overflow
    services.AddDbContext<ParallelCrmDbContext>(o => o.UseSqlite(_connection));
    services.RegisterPlatformDbContextHierarchy<ParallelCrmDbContext>();
    services.RegisterPlatformDbContextHierarchy<ParallelCrmDbContext>();

    services.AddDbContext<ParallelOrgDbContext>(o => o.UseSqlite(_connection));
    services.RegisterPlatformDbContextHierarchy<ParallelOrgDbContext>();

    services.AddDbContext<ParallelAppDbContext>(o => o.UseSqlite(_connection));
    services.RegisterPlatformDbContextHierarchy<ParallelAppDbContext>();

    services.AddDbContext<ParallelEcomDbContext>(o => o.UseSqlite(_connection));
    services.RegisterPlatformDbContextHierarchy<ParallelEcomDbContext>();

    await using var provider = services.BuildServiceProvider();

    await using (var crmScope = provider.CreateAsyncScope())
    {
      var dbScope = crmScope.ServiceProvider.GetRequiredService<DbScope>();
      dbScope.Bind(typeof(ParallelCrmDbContext));

      var appDb = crmScope.ServiceProvider.GetRequiredService<ParallelAppDbContext>();
      var orgDb = crmScope.ServiceProvider.GetRequiredService<ParallelOrgDbContext>();
      var platformDb = crmScope.ServiceProvider.GetRequiredService<PlatformDbContext>();
      var crmDb = crmScope.ServiceProvider.GetRequiredService<ParallelCrmDbContext>();

      Assert.IsType<ParallelCrmDbContext>(appDb);
      Assert.Same(crmDb, appDb);
      Assert.Same(crmDb, orgDb);
      Assert.Same(crmDb, platformDb);

      Assert.Throws<InvalidOperationException>(() => dbScope.Bind(typeof(ParallelEcomDbContext)));
    }

    await using (var ecomScope = provider.CreateAsyncScope())
    {
      var dbScope = ecomScope.ServiceProvider.GetRequiredService<DbScope>();
      dbScope.Bind(typeof(ParallelEcomDbContext));

      var appDb = ecomScope.ServiceProvider.GetRequiredService<ParallelAppDbContext>();
      var ecomDb = ecomScope.ServiceProvider.GetRequiredService<ParallelEcomDbContext>();

      Assert.IsType<ParallelEcomDbContext>(appDb);
      Assert.Same(ecomDb, appDb);
    }

    await using (var pureAppScope = provider.CreateAsyncScope())
    {
      var dbScope = pureAppScope.ServiceProvider.GetRequiredService<DbScope>();
      dbScope.Bind(typeof(ParallelAppDbContext));
      var appDb = pureAppScope.ServiceProvider.GetRequiredService<ParallelAppDbContext>();
      Assert.IsType<ParallelAppDbContext>(appDb);
    }
  }

  [Fact]
  public async Task UnscopedNonSystemContext_BlocksWrites_AndTenantIdIsImmutableEvenInSystemScope()
  {
    await using var unscopedDb = CreateDbContext(new UnscopedTenantContext(tenantId: null, isSystemScope: false));

    unscopedDb.Orders.Add(new TestOrder { Name = "Forged", Amount = 10m, TenantId = 1 });
    await Assert.ThrowsAsync<InvalidOperationException>(() => unscopedDb.SaveChangesAsync());
    unscopedDb.ChangeTracker.Clear();

    unscopedDb.AppAgentMemory.Add(new AppAgentMemory { Title = "GlobalLeak", Kind = "Fact", Content = "Leak" });
    await Assert.ThrowsAsync<InvalidOperationException>(() => unscopedDb.SaveChangesAsync());

    await using var t1Db = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: 10));
    var order = new TestOrder
    {
      Name = "WallClockTest",
      Amount = 25m,
      DeliveryDate = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc)
    };
    t1Db.Orders.Add(order);
    await t1Db.SaveChangesAsync();

    // In-memory entity DateTime matches reloaded DB Unspecified kind immediately after SaveChangesAsync
    Assert.Equal(DateTimeKind.Unspecified, order.DeliveryDate!.Value.Kind);

    await using var sysDb = CreateDbContext(new UnscopedTenantContext(tenantId: null, isSystemScope: true));
    var sysTracked = await sysDb.Orders.FirstAsync(o => o.Id == order.Id);
    sysTracked.TenantId = 2;
    sysTracked.Name = "UpdatedInSysScope";
    await sysDb.SaveChangesAsync();

    var reloaded = await sysDb.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
    Assert.Equal(1, reloaded.TenantId);
    Assert.Equal("UpdatedInSysScope", reloaded.Name);
  }

  [Fact]
  public async Task GlobalSystemAuditRow_StoresNullTenantId_AndRemainsHiddenFromRegularTenants()
  {
    await using var systemDb = CreateDbContext(new UnscopedTenantContext(tenantId: null, isSystemScope: true));
    systemDb.AppLanguage.Add(new AppLanguage { Handle = "De", Code = "de-DE", Name = "Deutsch", IsActive = true });
    await systemDb.SaveChangesAsync();

    var globalChange = Assert.Single(await systemDb.AppPlatformChange.AsNoTracking().ToListAsync());
    Assert.Null(globalChange.TenantId);

    await using var tenantDb = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: 10));
    Assert.Empty(await tenantDb.AppPlatformChange.AsNoTracking().ToListAsync());
  }

  [Fact]
  public void SynchronousSaveChanges_ThrowsNotSupportedException()
  {
    using var db = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: 10));
    Assert.Throws<NotSupportedException>(() => db.SaveChanges());
  }
}
