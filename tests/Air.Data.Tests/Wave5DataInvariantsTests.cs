using Microsoft.Extensions.DependencyInjection;
using Platform.Data.Tests.Internals;

namespace Platform.Data.Tests;

public sealed class Wave5DataInvariantsTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave5DataInvariantsTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
  }

  public void Dispose() => _connection.Dispose();

  private TestDbContext CreateDbContext(ITenantContext? tenant = null, TimeProvider? time = null)
  {
    var builder = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection);
    if (time is not null)
    {
      var sp = new ServiceCollection().AddSingleton(time).BuildServiceProvider();
      builder.UseApplicationServiceProvider(sp);
    }

    var db = new TestDbContext(builder.Options, tenant);
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public async Task ReadOnlyEntities_RejectNonSystemWrites_WhileBaseAppTenantRemainsEditable_AndUsesDbTime()
  {
    Assert.False(typeof(IReadOnlyEntity).IsAssignableFrom(typeof(BaseAppTenant)));
    Assert.True(typeof(IReadOnlyEntity).IsAssignableFrom(typeof(AppLanguage)));

    var unpersisted = new TestDataTenant { Handle = "Draft", Name = "Draft" };
    Assert.Equal(default, unpersisted.CreatedAt);

    var fixedNow = new DateTimeOffset(2026, 11, 14, 9, 15, 0, TimeSpan.Zero);
    var clock = new FixedDataTimeProvider(fixedNow);

    await using (var sysDb = CreateDbContext(new UnscopedTenantContext(tenantId: null, isSystemScope: true), clock))
    {
      Assert.Same(clock, sysDb.Time);
      sysDb.AppLanguage.Add(new AppLanguage { Id = 1, Handle = "En", Code = "en-US", Name = "English", IsActive = true });
      sysDb.Tenants.Add(new TestDataTenant { Id = 1, Handle = "Main", Name = "Main Org", IsActive = true });
      await sysDb.SaveChangesAsync();
    }

    await using var appDb = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: 10), clock);

    // 1. Adding IReadOnlyEntity outside system scope throws
    appDb.AppLanguage.Add(new AppLanguage { Id = 2, Handle = "Fr", Code = "fr-FR", Name = "French", IsActive = true });
    var addEx = await Assert.ThrowsAsync<InvalidOperationException>(() => appDb.SaveChangesAsync());
    Assert.Equal("AppLanguage is read-only.", addEx.Message);
    appDb.ChangeTracker.Clear();

    // 2. Modifying IReadOnlyEntity outside system scope throws
    var lang = await appDb.AppLanguage.FirstAsync(l => l.Handle == "En");
    lang.Name = "Mutated";
    await Assert.ThrowsAsync<InvalidOperationException>(() => appDb.SaveChangesAsync());
    appDb.ChangeTracker.Clear();

    // 3. Deleting IReadOnlyEntity outside system scope throws
    var langToDelete = await appDb.AppLanguage.FirstAsync(l => l.Handle == "En");
    appDb.AppLanguage.Remove(langToDelete);
    await Assert.ThrowsAsync<InvalidOperationException>(() => appDb.SaveChangesAsync());
    appDb.ChangeTracker.Clear();

    // 4. BaseAppTenant remains editable outside system scope and stamps CreatedAt from db.Time
    var mainTenant = await appDb.Tenants.FirstAsync(t => t.Handle == "Main");
    Assert.Equal(fixedNow, mainTenant.CreatedAt);
    mainTenant.Name = "Main Org Updated";
    var extraTenant = new TestDataTenant { Id = 2, Handle = "Branch", Name = "Branch Org", IsActive = true };
    appDb.Tenants.Add(extraTenant);
    await appDb.SaveChangesAsync();

    Assert.Equal(fixedNow, extraTenant.CreatedAt);
    Assert.Equal("Main Org Updated", (await appDb.Tenants.AsNoTracking().FirstAsync(t => t.Id == mainTenant.Id)).Name);
  }

  [Fact]
  public async Task IsRootGuard_FailsClosedWhenUserIdIsNull_RemovesId1Magic_AndLoadsCrossTenantUserNames()
  {
    long nonRootId1;
    long rootUserId;
    long peerRootUserId;

    await using (var sysDb = CreateDbContext(new UnscopedTenantContext(tenantId: 1, isSystemScope: true)))
    {
      // Intentionally insert non-root user FIRST so it receives Id == 1!
      var firstNonRoot = new TestDataUser { TenantId = 1, Login = "ordinary1", Name = "Ordinary One", IsRoot = false, IsActive = true };
      var rootTarget = new TestDataUser { TenantId = 1, Login = "roottarget", Name = "Root Target", IsRoot = true, IsActive = true };
      var peerRoot = new TestDataUser { TenantId = 2, Login = "peerroot", Name = "Cross Tenant Root", IsRoot = true, IsActive = true };
      sysDb.Users.AddRange(firstNonRoot, rootTarget, peerRoot);
      await sysDb.SaveChangesAsync();

      nonRootId1 = firstNonRoot.Id;
      rootUserId = rootTarget.Id;
      peerRootUserId = peerRoot.Id;
      Assert.Equal(1L, nonRootId1);
    }

    // 1. UserId == null (background/webhook context) MUST fail closed when modifying root user
    await using (var nullUserDb = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: null)))
    {
      var target = await nullUserDb.Users.FirstAsync(u => u.Id == rootUserId);
      target.Name = "ForgedByNullUser";
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => nullUserDb.SaveChangesAsync());
      Assert.Equal("The root user cannot be modified by another user.", ex.Message);
    }

    // 2. Non-root user with Id == 1 MUST NOT bypass root guard (no actorUserId == 1 magic)
    await using (var nonRootId1Db = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: nonRootId1)))
    {
      var target = await nonRootId1Db.Users.FirstAsync(u => u.Id == rootUserId);
      target.Name = "ForgedByUserId1";
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => nonRootId1Db.SaveChangesAsync());
      Assert.Equal("The root user cannot be modified by another user.", ex.Message);
    }

    // 3. Actual verified root user (even from another tenant via IgnoreQueryFilters) CAN update another root user,
    //    and LoadUserNamesAsync resolves user names across tenants via IgnoreTenantFilter()
    await using (var verifiedRootDb = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: peerRootUserId)))
    {
      var target = await verifiedRootDb.Users.FirstAsync(u => u.Id == rootUserId);
      target.Name = "UpdatedByVerifiedRoot";
      await verifiedRootDb.SaveChangesAsync();

      var names = await verifiedRootDb.LoadUserNamesAsync([nonRootId1, peerRootUserId]);
      Assert.Equal("Ordinary One", names[nonRootId1]);
      Assert.Equal("Cross Tenant Root", names[peerRootUserId]);
    }
  }

  [Fact]
  public async Task AppAgentMemory_BlocksSeedWritesOutsideSystemScope_AndBlocksPersonalMemoryWithoutUser()
  {
    long seededId;
    long sharedRuleId;

    await using (var sysDb = CreateDbContext(new UnscopedTenantContext(tenantId: 1, isSystemScope: true)))
    {
      var seedMem = new AppAgentMemory { TenantId = 1, Kind = AgentMemoryKinds.Seed, Title = "Domain Guide", Content = "Core" };
      var ruleMem = new AppAgentMemory { TenantId = 1, Kind = AgentMemoryKinds.Rule, Title = "Org Rule", Content = "Always check VAT" };
      sysDb.AppAgentMemory.AddRange(seedMem, ruleMem);
      await sysDb.SaveChangesAsync();
      seededId = seedMem.Id;
      sharedRuleId = ruleMem.Id;
    }

    // 1. Non-system user cannot insert, modify, or delete Seed memory (even changing Kind away from Seed)
    await using (var userDb = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: 10)))
    {
      userDb.AppAgentMemory.Add(new AppAgentMemory { Kind = AgentMemoryKinds.Seed, Title = "FakeSeed", Content = "X" });
      await Assert.ThrowsAsync<InvalidOperationException>(() => userDb.SaveChangesAsync());
      userDb.ChangeTracker.Clear();

      var trackedSeed = await userDb.AppAgentMemory.FirstAsync(m => m.Id == seededId);
      trackedSeed.Kind = AgentMemoryKinds.Fact;
      await Assert.ThrowsAsync<InvalidOperationException>(() => userDb.SaveChangesAsync());
      userDb.ChangeTracker.Clear();

      var deleteSeed = await userDb.AppAgentMemory.FirstAsync(m => m.Id == seededId);
      userDb.AppAgentMemory.Remove(deleteSeed);
      await Assert.ThrowsAsync<InvalidOperationException>(() => userDb.SaveChangesAsync());
      userDb.ChangeTracker.Clear();
    }

    // 2. Context with TenantId = 1 and UserId = null CANNOT insert or convert personal memory (prevents tenant-wide leak)
    await using (var noUserDb = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: null)))
    {
      noUserDb.AppAgentMemory.Add(new AppAgentMemory { Kind = AgentMemoryKinds.Fact, Title = "PersonalSecret", Content = "Leak?" });
      await Assert.ThrowsAsync<InvalidOperationException>(() => noUserDb.SaveChangesAsync());
      noUserDb.ChangeTracker.Clear();

      var sharedRule = await noUserDb.AppAgentMemory.FirstAsync(m => m.Id == sharedRuleId);
      sharedRule.Kind = AgentMemoryKinds.Preference;
      await Assert.ThrowsAsync<InvalidOperationException>(() => noUserDb.SaveChangesAsync());
      noUserDb.ChangeTracker.Clear();

      // Shared tenant Rule (UserId == null) remains allowed when UserId == null
      noUserDb.AppAgentMemory.Add(new AppAgentMemory { Kind = AgentMemoryKinds.Rule, Title = "AutoRule", Content = "Shared" });
      await noUserDb.SaveChangesAsync();
    }
  }

  [Fact]
  public async Task UnsignedEnumFilter_Throws400OnNegative_AndChangeJournalConsolidatesByClrTypeAndIdWithByteComparison()
  {
    await using var db = CreateDbContext(new FixedTenantContext(tenantId: 1, userId: 10));

    TestOrder seeded;
    using (db.BeginMutationRun())
    {
      seeded = new TestOrder
      {
        Name = "Initial",
        Amount = 100m,
        Tier = TestUnsignedTier.Basic,
        PayloadHash = [1, 2, 3, 4]
      };
      db.Orders.Add(seeded);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    // 1. Negative integer "-1" on unsigned byte enum throws QueryRequestException (400), never ArgumentException (500)
    var map = QueryMap.FromEntity<TestOrder>().Build();
    var provider = DataProvider.Create(db.Orders.AsNoTracking(), map);
    await Assert.ThrowsAsync<QueryRequestException>(() =>
      provider.GetPageAsync(new ListRequest
      {
        Filter = FilterNode.Rule("tier", FilterOperator.Equals, "-1")
      }));

    // 2. Separate entity instances across ChangeTracker.Clear() within one MutationRun merge into ONE journal entry
    db.ChangeTracker.Clear();
    Guid runId;
    using (db.BeginMutationRun())
    {
      runId = db.CurrentMutationRunId!.Value;

      var firstLoad = await db.Orders.FirstAsync(o => o.Id == seeded.Id);
      firstLoad.Amount = 200m;
      await db.SaveChangesAsync();

      db.ChangeTracker.Clear();

      var secondLoad = await db.Orders.FirstAsync(o => o.Id == seeded.Id);
      secondLoad.Amount = 350m;
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    var merged = Assert.Single(await db.AppPlatformChange.AsNoTracking().Where(c => c.RunId == runId).ToListAsync());
    Assert.Equal(ChangeOperations.Modified, merged.Operation);
    Assert.Contains("100", merged.Before ?? "");
    Assert.Contains("350", merged.After ?? "");

    // 3. Reassigning a fresh byte[] with identical bytes produces zero noisy AppPlatformChange diff
    db.ChangeTracker.Clear();
    Guid byteRunId;
    using (db.BeginMutationRun())
    {
      byteRunId = db.CurrentMutationRunId!.Value;
      var tracked = await db.Orders.FirstAsync(o => o.Id == seeded.Id);
      tracked.PayloadHash = [1, 2, 3, 4];
      db.Entry(tracked).Property(x => x.PayloadHash).IsModified = true;
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    Assert.Empty(await db.AppPlatformChange.AsNoTracking().Where(c => c.RunId == byteRunId).ToListAsync());
  }
}
