using Microsoft.EntityFrameworkCore.Infrastructure;
using Platform.Data.Tests.Internals;

namespace Platform.Data.Tests;

public sealed class PlatformDataTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public PlatformDataTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
  }

  public void Dispose() => _connection.Dispose();

  private TestDbContext CreateDbContext(long tenantId = 1, long userId = 10)
  {
    var options = new DbContextOptionsBuilder<TestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var tenant = new FixedTenantContext(tenantId, userId);
    var db = new TestDbContext(options, tenant);
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public async Task MultiFlushConsolidation_IncrementsVerOnce_AndMergesJournalEntries()
  {
    await using var db = CreateDbContext();

    var existingOrder = new TestOrder { Name = "Order-1", Amount = 100m };
    var transientOrder = new TestOrder { Name = "Temp", Amount = 50m };
    using (db.BeginMutationRun())
    {
      db.Orders.Add(existingOrder);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    Assert.Equal(1, existingOrder.Ver);
    var firstRunChanges = await db.AppPlatformChange.AsNoTracking().ToListAsync();
    Assert.Single(firstRunChanges);
    var firstRunId = firstRunChanges[0].RunId;

    using (db.BeginMutationRun())
    {
      existingOrder.Amount = 200m;
      var newOrder = new TestOrder { Name = "Order-2", Amount = 150m };
      db.Orders.Add(newOrder);
      db.Orders.Add(transientOrder);
      await db.SaveChangesAsync();

      existingOrder.Amount = 300m;
      newOrder.Amount = 250m;
      await db.SaveChangesAsync();

      db.Orders.Remove(transientOrder);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();

      Assert.Equal(2, existingOrder.Ver);
      Assert.Equal(1, newOrder.Ver);
    }

    var allChanges = await db.AppPlatformChange.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
    var secondRunChanges = allChanges.Where(c => c.RunId != firstRunId).ToList();
    Assert.Equal(2, secondRunChanges.Count);

    var updateRow = Assert.Single(secondRunChanges, c => c.RowId == existingOrder.Id);
    Assert.Equal(ChangeOperations.Modified, updateRow.Operation);
    Assert.Contains("100", updateRow.Before ?? "");
    Assert.Contains("300", updateRow.After ?? "");

    var createRow = Assert.Single(secondRunChanges, c => c.RowId != existingOrder.Id);
    Assert.Equal(ChangeOperations.Added, createRow.Operation);
    Assert.Null(createRow.Before);
    Assert.Contains("250", createRow.After ?? "");
  }

  [Fact]
  public async Task CascadeLoader_PreloadsCascadeAndSetNullDependents_IntoChangeJournal()
  {
    await using var db = CreateDbContext();

    TestOrder order;
    TestOrderLine cascadeLine;
    TestOrderNote setNullNote;
    using (db.BeginMutationRun())
    {
      order = new TestOrder { Name = "ParentOrder", Amount = 500m };
      db.Orders.Add(order);
      await db.SaveChangesAsync();

      cascadeLine = new TestOrderLine { OrderId = order.Id, Title = "Item A" };
      setNullNote = new TestOrderNote { OrderId = order.Id, Note = "Keep me with null FK" };
      db.OrderLines.Add(cascadeLine);
      db.OrderNotes.Add(setNullNote);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    await using var dbDelete = CreateDbContext();
    Guid deleteRunId;
    using (dbDelete.BeginMutationRun())
    {
      deleteRunId = dbDelete.CurrentMutationRunId!.Value;
      var toDelete = await dbDelete.Orders.FirstAsync(o => o.Id == order.Id);
      dbDelete.Orders.Remove(toDelete);
      await dbDelete.SaveChangesAsync();
      await dbDelete.FlushMutationJournalAsync();
    }

    var deleteChanges = await dbDelete.AppPlatformChange
      .AsNoTracking()
      .Where(c => c.RunId == deleteRunId)
      .ToListAsync();

    Assert.Contains(deleteChanges, c => c.RowId == order.Id && c.Operation == ChangeOperations.Deleted);
    Assert.Contains(deleteChanges, c => c.RowId == cascadeLine.Id && c.Operation == ChangeOperations.Deleted);
    Assert.Contains(deleteChanges, c => c.RowId == setNullNote.Id && c.Operation == ChangeOperations.Modified);
  }

  [Fact]
  public async Task NamedTenantFilter_RemainsActive_WhenCustomNamedFilterIsIgnored()
  {
    await using (var db1 = CreateDbContext(tenantId: 1))
    {
      db1.Orders.Add(new TestOrder { Name = "T1-Active", Amount = 10m });
      db1.Orders.Add(new TestOrder { Name = "T1-Archived", Amount = 20m, IsArchived = true });
      await db1.SaveChangesAsync();
    }

    await using (var db2 = CreateDbContext(tenantId: 2))
    {
      db2.Orders.Add(new TestOrder { Name = "T2-Active", Amount = 30m });
      db2.Orders.Add(new TestOrder { Name = "T2-Archived", Amount = 40m, IsArchived = true });
      await db2.SaveChangesAsync();
    }

    await using var readerT1 = CreateDbContext(tenantId: 1);
    var defaultRows = await readerT1.Orders.ToListAsync();
    Assert.Single(defaultRows);
    Assert.Equal("T1-Active", defaultRows[0].Name);

    var withArchived = await readerT1.Orders.IgnoreQueryFilters(["ArchivedFilter"]).ToListAsync();
    Assert.Equal(2, withArchived.Count);
    Assert.All(withArchived, o => Assert.Equal(1, o.TenantId));
  }

  [Fact]
  public async Task TextMatch_PreservesPercentLiteral_AndRevertedMultiFlushSkipsPhantomJournalRow()
  {
    await using var db = CreateDbContext();

    var order1 = new TestOrder { Name = "Discount 50%", Amount = 100m };
    var order2 = new TestOrder { Name = "Discount 500", Amount = 200m };
    using (db.BeginMutationRun())
    {
      db.Orders.AddRange(order1, order2);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    var map = QueryMap.FromEntity<TestOrder>().Build();
    var caseInsensitiveQuery = map.Plan(new ListRequest { Search = "discount 50%" }).Filter(db.Orders);
    var matchedCi = await caseInsensitiveQuery.ToListAsync();
    Assert.Single(matchedCi);
    Assert.Equal(order1.Id, matchedCi[0].Id);

    var queryWithPercent = map.Plan(new ListRequest { Search = "50%" }, useCollationComparison: true).Filter(db.Orders);
    Assert.DoesNotContain("ToLower", queryWithPercent.Expression.ToString(), StringComparison.OrdinalIgnoreCase);
    var matched = await queryWithPercent.ToListAsync();
    Assert.Single(matched);
    Assert.Equal(order1.Id, matched[0].Id);

    Guid revertRunId;
    using (db.BeginMutationRun())
    {
      revertRunId = db.CurrentMutationRunId!.Value;
      order1.Amount = 999m;
      await db.SaveChangesAsync();

      order1.Amount = 100m;
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    var revertChanges = await db.AppPlatformChange.Where(c => c.RunId == revertRunId).ToListAsync();
    Assert.Empty(revertChanges);
  }

  [Fact]
  public async Task StreamChunksAsync_StreamsSinglePassBoundedChunks_AndRespectsLimit()
  {
    await using var db = CreateDbContext();
    for (var i = 1; i <= 7; i++)
    {
      db.Orders.Add(new TestOrder { Name = $"Order-{i:D2}", Amount = i * 10m });
    }

    await db.SaveChangesAsync();

    var map = QueryMap.FromEntity<TestOrder>()
      .DefaultSort(SortRule.Asc(nameof(TestOrder.Name)))
      .Build();
    var provider = DataProvider.Create(db.Orders.AsNoTracking(), map);

    var chunks = new List<IReadOnlyList<TestOrder>>();
    await foreach (var chunk in provider.StreamChunksAsync(new ListRequest(), limit: 5, chunkSize: 2))
    {
      chunks.Add(chunk);
    }

    Assert.Equal(3, chunks.Count);
    Assert.Equal([10m, 20m], chunks[0].Select(o => o.Amount).ToArray());
    Assert.Equal([30m, 40m], chunks[1].Select(o => o.Amount).ToArray());
    Assert.Equal([50m], chunks[2].Select(o => o.Amount).ToArray());

    var exactChunks = new List<IReadOnlyList<TestOrder>>();
    await foreach (var chunk in provider.StreamChunksAsync(new ListRequest(), limit: 4, chunkSize: 2))
    {
      exactChunks.Add(chunk);
    }

    Assert.Equal(2, exactChunks.Count);
    Assert.Equal([10m, 20m], exactChunks[0].Select(o => o.Amount).ToArray());
    Assert.Equal([30m, 40m], exactChunks[1].Select(o => o.Amount).ToArray());

    var emptyChunks = new List<IReadOnlyList<TestOrder>>();
    await foreach (var chunk in provider.StreamChunksAsync(new ListRequest { Search = "non-existent" }, limit: 10, chunkSize: 3))
    {
      emptyChunks.Add(chunk);
    }

    Assert.Empty(emptyChunks);
  }

  [Fact]
  public async Task SqlServerDesignTimeFactory_MigratorCheck_AndEmptyBulkImport_WorkOffline()
  {
    var factory = new TestSqlServerDesignTimeFactory();
    await using var sqlDb = factory.CreateDbContext([]);

    Assert.Contains("platform_design_time", sqlDb.Database.GetConnectionString(), StringComparison.Ordinal);

    var relationalExt = sqlDb.GetService<IDbContextOptions>()
      .Extensions
      .OfType<RelationalOptionsExtension>()
      .Single();
    Assert.Equal("__Test_EFMigrationsHistory", relationalExt.MigrationsHistoryTableName);

    var checkEx = Assert.Throws<InvalidOperationException>(() => PlatformMigrator.Check([sqlDb]));
    Assert.Contains("has no EF Core migrations", checkEx.Message, StringComparison.Ordinal);

    var imported = await sqlDb.BulkImportAsync(AsyncEnumerable.Empty<TestOrder>());
    Assert.Equal(0, imported);
  }
}
