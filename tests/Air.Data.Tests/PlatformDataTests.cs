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
}
