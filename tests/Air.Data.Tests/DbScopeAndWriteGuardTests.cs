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

  private TestDbContext CreateDbContext(ITenantContext? tenant = null)
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

  [Fact]
  public async Task DefaultTenantContext_AndUnscopedAsyncLocalTenantContext_AreFailClosedForReadsAndWrites()
  {
    ITenantContext defaultCtx = new DefaultInterfaceTenantContext(tenantId: null);
    Assert.False(defaultCtx.IsSystemScope);
    Assert.Null(AsyncLocalTenantContext.Instance.TenantId);
    Assert.False(AsyncLocalTenantContext.Instance.IsSystemScope);
    Assert.False(AsyncLocalTenantContext.Instance.HasExplicitSystemScope);

    await using (var seedSys = CreateDbContext(new UnscopedTenantContext(tenantId: null, isSystemScope: true)))
    {
      seedSys.Orders.AddRange(
        new TestOrder { Name = "T1-Order", Amount = 10m, TenantId = 1 },
        new TestOrder { Name = "T2-Order", Amount = 20m, TenantId = 2 });
      seedSys.Roles.AddRange(
        new TestRole { Name = "Global-Role", TenantId = null },
        new TestRole { Name = "T1-Role", TenantId = 1 },
        new TestRole { Name = "T2-Role", TenantId = 2 });
      await seedSys.SaveChangesAsync();
    }

    // Unscoped AsyncLocalTenantContext (TenantId == null, no UseSystemScope()) must fail closed
    await using var ambientDb = CreateDbContext(tenant: null);
    Assert.Same(AsyncLocalTenantContext.Instance, ambientDb.TenantContext);

    Assert.Empty(await ambientDb.Orders.AsNoTracking().ToListAsync());

    var unscopedRoles = await ambientDb.Roles.AsNoTracking().ToListAsync();
    var globalRoleOnly = Assert.Single(unscopedRoles);
    Assert.Equal("Global-Role", globalRoleOnly.Name);
    Assert.Null(globalRoleOnly.TenantId);

    Assert.Equal(2, await ambientDb.Orders.AsNoTracking().IgnoreTenantFilter().CountAsync());
    Assert.Equal(3, await ambientDb.Roles.AsNoTracking().IgnoreTenantFilter().CountAsync());

    ambientDb.Orders.Add(new TestOrder { Name = "BlockedOrder", Amount = 5m, TenantId = 1 });
    await Assert.ThrowsAsync<InvalidOperationException>(() => ambientDb.SaveChangesAsync());
    ambientDb.ChangeTracker.Clear();

    ambientDb.Roles.Add(new TestRole { Name = "BlockedRole", TenantId = null });
    await Assert.ThrowsAsync<InvalidOperationException>(() => ambientDb.SaveChangesAsync());
    ambientDb.ChangeTracker.Clear();

    var trackedGlobalRole = await ambientDb.Roles.FirstAsync(r => r.TenantId == null);
    trackedGlobalRole.Name = "MutateGlobalWithoutSystemScope";
    await Assert.ThrowsAsync<InvalidOperationException>(() => ambientDb.SaveChangesAsync());
    ambientDb.ChangeTracker.Clear();

    // Explicit Use(tenantId: null) also remains fail-closed
    using (AsyncLocalTenantContext.Use(tenantId: null))
    {
      Assert.False(AsyncLocalTenantContext.Instance.IsSystemScope);
      Assert.Empty(await ambientDb.Orders.AsNoTracking().ToListAsync());
    }

    // Explicit UseSystemScope() grants cross-tenant read & system write access
    using (AsyncLocalTenantContext.UseSystemScope(userId: 99, correlationId: "sys-op"))
    {
      Assert.True(AsyncLocalTenantContext.Instance.IsSystemScope);
      Assert.True(AsyncLocalTenantContext.Instance.HasExplicitSystemScope);
      Assert.Equal(2, await ambientDb.Orders.AsNoTracking().CountAsync());
      Assert.Equal(3, await ambientDb.Roles.AsNoTracking().CountAsync());

      ambientDb.Roles.Add(new TestRole { Name = "SystemSeededGlobalRole", TenantId = null });
      ambientDb.Orders.Add(new TestOrder { Name = "SystemSeededT2Order", Amount = 50m, TenantId = 2 });
      await ambientDb.SaveChangesAsync();
    }

    // Scope disposal restores fail-closed state
    Assert.False(AsyncLocalTenantContext.Instance.IsSystemScope);
    Assert.Empty(await ambientDb.Orders.AsNoTracking().ToListAsync());
  }
}
