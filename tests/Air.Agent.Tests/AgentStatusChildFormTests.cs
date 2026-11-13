using Platform.Agent.Tests.Internals;

namespace Platform.Agent.Tests;

public sealed class AgentStatusChildFormTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public AgentStatusChildFormTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    ModuleCatalog.Instance.Register(new AgentTestModuleImpl());
  }

  public void Dispose() => _connection.Dispose();

  private (IServiceProvider Services, AgentTestDbContext Db) CreateServices()
  {
    var options = new DbContextOptionsBuilder<AgentTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var tenant = new FixedTenant(1, 10, new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase));
    var user = new FixedUser(10, "*");
    var db = new AgentTestDbContext(options, tenant);
    db.Database.EnsureCreated();

    var app = new TestPlatformAppService(db, user);
    var sc = new ServiceCollection();
    sc.AddSingleton<PlatformDbContext>(db);
    sc.AddSingleton<PlatformAppService>(app);
    sc.AddSingleton<ITenantContext>(tenant);
    sc.AddSingleton<IUserContext>(user);
    sc.AddSingleton<McpExecutionBridge>();
    sc.AddSingleton(new AgentStore());
    return (sc.BuildServiceProvider(), db);
  }

  [Fact]
  public async Task StatusNavigationChildForm_CreatesOrderWithLines_ReplacesLinesInDraft_AndRejectsStagingWhenFrozenOrFinal()
  {
    var (services, db) = CreateServices();
    var orderFormPath = ServicePathResolver.Resolve(typeof(OrderFormService));
    var expectedReadOnly = Localizer.Instance["FormReadOnly"];

    var draftStatus = new AgentTestOrderStatus { Id = 1, Name = "Draft", IsEditable = true, IsFinal = false };
    var confirmedStatus = new AgentTestOrderStatus { Id = 2, Name = "Confirmed", IsEditable = false, IsFinal = false };
    var completedStatus = new AgentTestOrderStatus { Id = 3, Name = "Completed", IsEditable = false, IsFinal = true };
    using (db.BeginMutationRun())
    {
      db.OrderStatuses.AddRange(draftStatus, confirmedStatus, completedStatus);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    db.ChangeTracker.Clear();

    // 1. Create order + child lines in one atomic proposal when BeforeSave only sets OrderStatusId (not OrderStatus navigation).
    var createArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-order",
      service = orderFormPath,
      values = new
      {
        defaultDiscountPercent = 10m,
        note = "Initial order",
        lines = new[]
        {
          new { itemName = "Item A", qty = 2m, price = 100m, discountPercent = 0m },
          new { itemName = "Item B", qty = 1m, price = 50m, discountPercent = 0m }
        }
      }
    });

    var createProposal = await AgentProposalBuilder.StageProposalAsync(createArgs, services, CancellationToken.None);
    var created = await AgentProposalExecutor.CommitProposalAsync(createProposal, services, CancellationToken.None);

    var savedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == created.RecordId);
    Assert.Equal(draftStatus.Id, savedOrder.OrderStatusId);
    Assert.Equal(250m, savedOrder.Amount);
    Assert.Equal(2, await db.OrderLines.CountAsync(l => l.OrderId == created.RecordId));

    // 2. Replace child lines on an editable Draft order (verifies BindParentAsync before CanDelete and parent-scoped AfterLoad defaults).
    var replaceArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-order",
      service = orderFormPath,
      recordId = created.RecordId,
      children = new
      {
        lines = new
        {
          mode = "replace",
          rows = new[]
          {
            new { itemName = "Item C", qty = 3m, price = 100m }
          }
        }
      }
    });

    var replaceProposal = await AgentProposalBuilder.StageProposalAsync(replaceArgs, services, CancellationToken.None);
    await AgentProposalExecutor.CommitProposalAsync(replaceProposal, services, CancellationToken.None);

    var replacedLines = await db.OrderLines.AsNoTracking().Where(l => l.OrderId == created.RecordId).ToListAsync();
    var singleLine = Assert.Single(replacedLines);
    Assert.Equal("Item C", singleLine.ItemName);
    Assert.Equal(10m, singleLine.DiscountPercent);
    Assert.Equal(270m, singleLine.Amount);

    var updatedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == created.RecordId);
    Assert.Equal(270m, updatedOrder.Amount);

    // 3. Move order to Confirmed (IsEditable = false, IsFinal = false): staging append or existing-row edits fails fast with FormReadOnly.
    using (db.BeginMutationRun())
    {
      var tracked = await db.Orders.SingleAsync(o => o.Id == created.RecordId);
      tracked.OrderStatusId = confirmedStatus.Id;
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    db.ChangeTracker.Clear();

    var appendOnConfirmedArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-order",
      service = orderFormPath,
      recordId = created.RecordId,
      children = new
      {
        lines = new
        {
          mode = "append",
          rows = new[] { new { itemName = "Item D", qty = 1m, price = 40m } }
        }
      }
    });
    var appendEx = await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await AgentProposalBuilder.StageProposalAsync(appendOnConfirmedArgs, services, CancellationToken.None));
    Assert.Equal(expectedReadOnly, appendEx.Message);

    var editExistingLineArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-order",
      service = orderFormPath,
      recordId = created.RecordId,
      children = new
      {
        lines = new
        {
          mode = "replace",
          rows = new[] { new { id = singleLine.Id, itemName = "Item C", qty = 5m, price = 100m } }
        }
      }
    });
    var editLineEx = await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await AgentProposalBuilder.StageProposalAsync(editExistingLineArgs, services, CancellationToken.None));
    Assert.Equal(expectedReadOnly, editLineEx.Message);

    // 4. Move order to Completed (IsFinal = true): staging parent edits also fails fast with FormReadOnly.
    using (db.BeginMutationRun())
    {
      var tracked = await db.Orders.SingleAsync(o => o.Id == created.RecordId);
      tracked.OrderStatusId = completedStatus.Id;
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    db.ChangeTracker.Clear();

    var editFinalArgs = JsonSerializer.SerializeToElement(new
    {
      sessionId = "sess-order",
      service = orderFormPath,
      recordId = created.RecordId,
      values = new { note = "Should fail" }
    });
    var editFinalEx = await Assert.ThrowsAsync<InvalidOperationException>(
      async () => await AgentProposalBuilder.StageProposalAsync(editFinalArgs, services, CancellationToken.None));
    Assert.Equal(expectedReadOnly, editFinalEx.Message);
  }
}
