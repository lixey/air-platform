using Platform.Api.Mutations.Internals;
using Platform.Api.Tests.Internals;

namespace Platform.Api.Tests;

public sealed class MutationAndServiceSecurityTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public MutationAndServiceSecurityTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    IdempotencyStore.Clear();
  }

  public void Dispose()
  {
    IdempotencyStore.Clear();
    _connection.Dispose();
  }

  private ApiTestDbContext CreateDbContext(long tenantId = 1, long userId = 10)
  {
    var options = new DbContextOptionsBuilder<ApiTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var db = new ApiTestDbContext(options, new FixedTenant(tenantId, userId));
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public async Task RunMutationAsync_IsReentrant_AndResetsStateOnRollback()
  {
    await using var db = CreateDbContext();

    var outerRunId = Guid.Empty;
    var innerRunId = Guid.Empty;
    await db.RunMutationAsync(async ct =>
    {
      outerRunId = db.CurrentMutationRunId!.Value;
      var item = new ApiTestItem { Name = "First" };
      db.Items.Add(item);
      await db.SaveChangesAsync(ct);

      await db.RunMutationAsync(async innerCt =>
      {
        innerRunId = db.CurrentMutationRunId!.Value;
        item.Name = "First-Updated";
        await db.SaveChangesAsync(innerCt);
      }, ct);
    });

    Assert.NotEqual(Guid.Empty, outerRunId);
    Assert.Equal(outerRunId, innerRunId);
    Assert.Equal(1, await db.Items.CountAsync());
    Assert.Single(await db.AppPlatformChange.Where(c => c.RunId == outerRunId).ToListAsync());

    await Assert.ThrowsAsync<InvalidOperationException>(async () => await db.RunMutationAsync(async ct =>
      {
        db.Items.Add(new ApiTestItem { Name = "ShouldRollback" });
        await db.SaveChangesAsync(ct);
        throw new InvalidOperationException("Boom");
      }));

    Assert.False(db.ChangeTracker.HasChanges());
    Assert.Null(db.CurrentMutationRunId);
    Assert.Equal(1, await db.Items.CountAsync());
  }

  [Fact]
  public async Task LockAsync_ThrowsInvalidOperationException_OutsideActiveTransaction()
  {
    await using var db = CreateDbContext();
    var ctx = new MutationContext(db);

    await Assert.ThrowsAsync<InvalidOperationException>(async () => await ctx.LockAsync<ApiTestItem>(CancellationToken.None, 1));
  }

  [Fact]
  public async Task RunIdempotentCreateAsync_ReplaysSameKey_AndRejectsConflictingPayload()
  {
    await using var db = CreateDbContext();
    var executionCount = 0;

    var res1 = await MutationRunner.RunIdempotentCreateAsync<ApiTestItem>(
      db,
      "demo.items-form",
      "idem-key-1",
      "{\"name\":\"Alpha\"}",
      async ct =>
      {
        executionCount++;
        var entity = new ApiTestItem { Name = "Alpha" };
        db.Items.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
      },
      async (replayedId, ct) => await db.Items.AsNoTracking().FirstOrDefaultAsync(e => e.Id == replayedId, ct));

    var res2 = await MutationRunner.RunIdempotentCreateAsync<ApiTestItem>(
      db,
      "demo.items-form",
      "idem-key-1",
      "{\"name\":\"Alpha\"}",
      async ct =>
      {
        executionCount++;
        var entity = new ApiTestItem { Name = "Alpha-Duplicate" };
        db.Items.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
      },
      async (replayedId, ct) => await db.Items.AsNoTracking().FirstOrDefaultAsync(e => e.Id == replayedId, ct));

    Assert.Equal(1, executionCount);
    Assert.Equal(res1.Id, res2.Id);
    Assert.Equal(1, await db.Items.CountAsync());

    await Assert.ThrowsAsync<IdempotencyConflictException>(async () => await MutationRunner.RunIdempotentCreateAsync<ApiTestItem>(
        db,
        "demo.items-form",
        "idem-key-1",
        "{\"name\":\"Beta\"}",
        _ => Task.FromResult(new ApiTestItem())));
  }

  [Fact]
  public void IsCallableServiceAction_BlocksArbitraryPlatformMethods_AndAllowsAttributedActions()
  {
    var internalHelperMethod = typeof(FakeAppService).GetMethod(nameof(FakeAppService.UnattributedHelper))!;
    var attributedMethod = typeof(FakeAppService).GetMethod(nameof(FakeAppService.ExplicitServiceAction))!;
    var requireActionMethod = typeof(FakeAppService).GetMethod(nameof(FakeAppService.GuardedAction))!;

    Assert.False(ServiceEndpointsExtensions.IsCallableServiceAction(internalHelperMethod));
    Assert.True(ServiceEndpointsExtensions.IsCallableServiceAction(attributedMethod));
    Assert.True(ServiceEndpointsExtensions.IsCallableServiceAction(requireActionMethod));
  }

  [Fact]
  public async Task MarkMutationRollbackOnly_RollsBackCommittedWrites_WhenRefusedResultReturned()
  {
    await using var db = CreateDbContext();

    var outcome = await db.RunMutationAsync(async ct =>
    {
      db.Items.Add(new ApiTestItem { Name = "DirtyWriteBeforeRefusal" });
      await db.SaveChangesAsync(ct);
      db.MarkMutationRollbackOnly();
      return MutationOutcome.Refused;
    });

    Assert.Equal(MutationOutcome.Refused, outcome);
    Assert.Equal(0, await db.Items.CountAsync());
    Assert.Equal(0, await db.AppPlatformChange.CountAsync());
  }
}
