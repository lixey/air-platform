using System.Text;
using Microsoft.AspNetCore.Http;
using Platform.Api.Host.Internals;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class Wave4ActionsFlowAndMasterDetailTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave4ActionsFlowAndMasterDetailTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    ModuleCatalog.Instance.Register(new TestAppModuleImpl());
  }

  public void Dispose() => _connection.Dispose();

  private AppTestDbContext CreateDbContext(long tenantId = 1, long userId = 10, params string[] rights)
  {
    var options = new DbContextOptionsBuilder<AppTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var effectiveRights = rights.Length > 0
      ? new HashSet<string>(rights, StringComparer.OrdinalIgnoreCase)
      : new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase);
    var db = new AppTestDbContext(options, new FixedTenant(tenantId, userId, effectiveRights));
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public async Task SetActions_UseActionsOf_AskAsyncReferenceValidation_Defaults_AndPermissionGates()
  {
    // 1. PlatformValidator rejects AllowEdit on ReadOnly feature / missing .Do(...)
    var badCatalog = new ModuleCatalog();
    badCatalog.Register(new MockBadSetActionModuleImpl());
    var badEx = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(badCatalog));
    Assert.Contains("MutateOnReadOnly", badEx.Message, StringComparison.Ordinal);

    await using var db = CreateDbContext();
    var fullApp = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var viewOnlyApp = new TestPlatformAppService(db, new FixedUser(11, "Test.OrderDocs.View"));

    var activeTag = new TestTag { Name = "VIP", IsActive = true };
    var inactiveTag = new TestTag { Name = "DisabledTag", IsActive = false };
    var openOrder = new TestInvoice { Number = "ORD-10", Status = 1, PrintedCount = 4 };
    var closedOrder = new TestInvoice { Number = "ORD-20", Status = 2, PrintedCount = 2 };
    db.Tags.AddRange(activeTag, inactiveTag);
    db.Invoices.AddRange(openOrder, closedOrder);
    await db.SaveChangesAsync();

    // 2. GetActionInputAsync enforces action permission and delegates UseActionsOf<TestOrderMasterFormService>()
    var roGrid = new TestOrderMasterGridService(db, viewOnlyApp);
    var deniedDefaults = await roGrid.GetActionInputAsync("stamp-note", new GridRequest { SelectedIds = [openOrder.Id] });
    Assert.Null(deniedDefaults);

    var grid = new TestOrderMasterGridService(db, fullApp);
    var resolvedDefaults = Assert.IsType<Dictionary<string, object?>>(
      await grid.GetActionInputAsync("stamp-note", new GridRequest { SelectedIds = [openOrder.Id] }));
    Assert.Equal("Ref:ORD-10", resolvedDefaults["note"]?.ToString());

    // 3. UseActionsOf projects row-level unavailable map on Grid rows when .When(...) is unsatisfied
    var page = await grid.GetDataAsync(new GridRequest { IsDataOnly = true }, CancellationToken.None);
    var closedRow = page.Rows.First(r => r.Id == closedOrder.Id);
    Assert.NotNull(closedRow.Unavailable);
    Assert.Contains("stampnote", closedRow.Unavailable.Keys);

    // 4. Ask<TInput> enforces async AsReference DB validation (inactive tag rejected with field path)
    var badTagPayload = JsonSerializer.SerializeToElement(new { tagId = inactiveTag.Id, note = "Test" });
    var refusedAsk = await grid.ExecuteDeclaredActionAsync(
      "stamp-note",
      new GridRequest { SelectedIds = [openOrder.Id], SelectedVersions = [openOrder.Ver], RawPayload = badTagPayload.GetRawText() },
      badTagPayload);
    Assert.NotNull(refusedAsk);
    Assert.Equal(MutationOutcome.Refused, refusedAsk.Outcome);
    Assert.Contains(refusedAsk.Messages ?? [], m => m.Path == "tagId");

    // 5. Valid Ask<TInput> executes delegated Form action via Grid and returns ShowNotice messages
    var goodTagPayload = JsonSerializer.SerializeToElement(new { tagId = activeTag.Id, note = "Urgent" });
    var committedAsk = await grid.ExecuteDeclaredActionAsync(
      "stamp-note",
      new GridRequest { SelectedIds = [openOrder.Id], SelectedVersions = [openOrder.Ver], RawPayload = goodTagPayload.GetRawText() },
      goodTagPayload);
    Assert.NotNull(committedAsk);
    Assert.Equal(MutationOutcome.Committed, committedAsk.Outcome);
    Assert.Equal("Stamped", committedAsk.Title);
    Assert.Contains(committedAsk.Messages ?? [], m => m.Detail == "Applied VIP");

    var updatedOrder = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == openOrder.Id);
    Assert.Equal("ORD-10-VIP-Urgent", updatedOrder.Number);

    // 6. WithoutSelection().WithoutTransaction() with AllowView() works for View-only user, while AllowEdit() refuses
    var pong = await roGrid.ExecuteDeclaredActionAsync("global-ping", new GridRequest(), null);
    Assert.NotNull(pong);
    Assert.Equal(MutationOutcome.Committed, pong.Outcome);
    Assert.Equal("Pong", pong.Title);

    var deniedBulk = await roGrid.ExecuteDeclaredActionAsync("bulk-reset", new GridRequest { SelectedIds = [openOrder.Id] }, null);
    Assert.NotNull(deniedBulk);
    Assert.Equal(MutationOutcome.Refused, deniedBulk.Outcome);
  }

  [Fact]
  public async Task StatusFlow_FromOpen_FinalStatus_IntState_AutoFollower_DedupSelection_AndLockOrder()
  {
    await using var db = CreateDbContext();
    var fullApp = new TestPlatformAppService(db, new FixedUser(10, "*"));

    // 1. FromOpen() includes open statuses (1, 2) and excludes FinalStatus (3)
    var events = new List<string>();
    var orderFlow = new TestOrderFlowService(db, events);
    Assert.Equal([1L, 2L], orderFlow.Cancel.From.OrderBy(x => x).ToArray());

    var ord1 = new TestInvoice { Number = "ORD-A", Status = 1 };
    var ord2 = new TestInvoice { Number = "ORD-B", Status = 1 };
    db.Invoices.AddRange(ord1, ord2);
    await db.SaveChangesAsync();

    // 2. Duplicate SelectedIds in reverse order are deduplicated and locked in ascending PK order BEFORE transition effect
    var masterGrid = new TestOrderMasterGridService(db, fullApp, events);
    var moveRes = await masterGrid.MoveStatusAsync(
      new GridRequest { SelectedIds = [ord2.Id, ord1.Id, ord2.Id] },
      "Complete",
      null,
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, moveRes.Outcome);
    Assert.Equal(2, moveRes.Count);
    Assert.Equal($"FlowLock:{ord1.Id},{ord2.Id}", events[0]);
    Assert.Equal("TransitionEffect:Complete", events[1]);

    // 3. Child int-based status flow cascades to parent via FlowFollowerRegistry (without FollowedBy) and populates AffectedDto
    var parentOrder = new TestInvoice { Number = "ORD-CASCADE", Status = 1 };
    db.Invoices.Add(parentOrder);
    await db.SaveChangesAsync();
    var parentVerBefore = parentOrder.Ver;

    var line1 = new TestInvoiceLine { InvoiceId = parentOrder.Id, Title = "Part 1", Amount = 40m, TierCode = 1 };
    var line2 = new TestInvoiceLine { InvoiceId = parentOrder.Id, Title = "Part 2", Amount = 60m, TierCode = 1 };
    db.InvoiceLines.AddRange(line1, line2);
    await db.SaveChangesAsync();

    var masterFormPath = ServicePathResolver.Resolve(typeof(TestOrderMasterFormService));
    var detailGrid = new TestOrderDetailGridService(db, fullApp);
    var cascadeMove = await detailGrid.MoveStatusAsync(
      new GridRequest
      {
        ParentService = masterFormPath,
        ParentId = parentOrder.Id,
        SelectedIds = [line1.Id, line2.Id],
        SelectedVersions = [line1.Ver, line2.Ver]
      },
      "fulfill",
      null,
      CancellationToken.None);

    Assert.Equal(MutationOutcome.Committed, cascadeMove.Outcome);
    var reloadedParent = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == parentOrder.Id);
    Assert.Equal(2L, reloadedParent.Status);
    Assert.True(reloadedParent.Ver > parentVerBefore);

    var affectedParent = Assert.Single(cascadeMove.Affected ?? [], a => a.Service == masterFormPath && a.Id == parentOrder.Id);
    Assert.Equal(parentVerBefore, affectedParent.PrevVer);
    Assert.Equal(reloadedParent.Ver, affectedParent.Ver);
  }

  [Fact]
  public async Task MasterDetail_DerivedRecalculation_IncrementsParentVer_PopulatesAffectedDto_AndSuppressesDerivedAuditNoise()
  {
    await using var db = CreateDbContext();
    var user = new FixedUser(10, "*");
    var fullApp = new TestPlatformAppService(db, user);

    TestInvoice parent;
    using (db.BeginMutationRun())
    {
      parent = new TestInvoice { Number = "ORD-MD-1", Status = 1, AuditedTotal = 0m };
      db.Invoices.Add(parent);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    var initialParentVer = parent.Ver;
    var changeCountBefore = await db.AppPlatformChange.CountAsync();
    var masterFormPath = ServicePathResolver.Resolve(typeof(TestOrderMasterFormService));
    var detailFormPath = ServicePathResolver.Resolve(typeof(TestOrderDetailFormService));

    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<PlatformDbContext>(db)
      .AddSingleton(db)
      .AddSingleton<IUserContext>(user)
      .AddSingleton<PlatformAppService>(fullApp)
      .AddTransient<TestOrderDetailFormService>()
      .AddTransient<TestOrderDetailGridService>()
      .BuildServiceProvider();

    var saveBody = JsonSerializer.Serialize(new
    {
      parentService = masterFormPath,
      parentId = parent.Id,
      value = new { title = "Alternator", amount = 125.50m, tierCode = 1 }
    });

    var saveHttp = new DefaultHttpContext { RequestServices = services };
    saveHttp.Request.Path = $"/api/{detailFormPath}/save";
    saveHttp.Request.ContentType = "application/json";
    saveHttp.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(saveBody));
    saveHttp.Response.Body = new MemoryStream();

    var detailFormInstance = services.GetRequiredService<TestOrderDetailFormService>();
    var saveResult = await EndpointExecutor.DispatchAsync(
      typeof(TestOrderDetailFormService),
      detailFormInstance,
      "save",
      TestAppModule.OrderDocs,
      saveHttp.Request,
      services,
      CancellationToken.None);
    await saveResult.ExecuteAsync(saveHttp);
    Assert.Equal(StatusCodes.Status200OK, saveHttp.Response.StatusCode);

    saveHttp.Response.Body.Position = 0;
    var savedDto = await JsonSerializer.DeserializeAsync<FormDataDto>(
      saveHttp.Response.Body,
      PlatformJsonOptions.DefaultOptions);
    Assert.NotNull(savedDto);
    Assert.Equal(MutationOutcome.Committed, savedDto.Outcome);

    var affectedOnCreate = Assert.Single(savedDto.Affected ?? [], a => a.Service == masterFormPath && a.Id == parent.Id);
    Assert.Equal(initialParentVer, affectedOnCreate.PrevVer);
    Assert.Equal(initialParentVer + 1, affectedOnCreate.Ver);

    var dbParentAfterCreate = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == parent.Id);
    Assert.Equal(initialParentVer + 1, dbParentAfterCreate.Ver);
    Assert.Equal(125.50m, dbParentAfterCreate.AuditedTotal);

    // Derived parent recalculation must NOT emit noisy AppPlatformChange rows for Invoices — only the child line Added row!
    var newChanges = await db.AppPlatformChange.OrderBy(c => c.Id).Skip(changeCountBefore).ToListAsync();
    var singleChange = Assert.Single(newChanges);
    Assert.Equal(nameof(AppTestDbContext.InvoiceLines), singleChange.Table);
    Assert.Equal(ChangeOperations.Added, singleChange.Operation);

    // Deleting the child line increments parent Ver again (2 -> 3) and returns AffectedDto with auditedTotal = 0
    var detailGrid = new TestOrderDetailGridService(db, fullApp);
    var delDto = await detailGrid.Delete(new GridRequest
    {
      ParentService = masterFormPath,
      ParentId = parent.Id,
      SelectedIds = [savedDto.Id!.Value]
    });
    Assert.Equal(MutationOutcome.Committed, delDto.Outcome);
    var affectedOnDelete = Assert.Single(delDto.Affected ?? [], a => a.Service == masterFormPath && a.Id == parent.Id);
    Assert.Equal(initialParentVer + 1, affectedOnDelete.PrevVer);
    Assert.Equal(initialParentVer + 2, affectedOnDelete.Ver);

    var dbParentAfterDelete = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == parent.Id);
    Assert.Equal(0m, dbParentAfterDelete.AuditedTotal);
    Assert.Equal(initialParentVer + 2, dbParentAfterDelete.Ver);
  }

  [Fact]
  public async Task ActionIdempotency_ReplaysIdenticalRequest_AndRejectsDifferentPayloadWith409()
  {
    await using var db = CreateDbContext();
    var user = new FixedUser(10, "*");
    var fullApp = new TestPlatformAppService(db, user);

    var tag = new TestTag { Name = "FAST", IsActive = true };
    var order = new TestInvoice { Number = "ORD-IDEM", Status = 1 };
    db.Tags.Add(tag);
    db.Invoices.Add(order);
    await db.SaveChangesAsync();

    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<PlatformDbContext>(db)
      .AddSingleton(db)
      .AddSingleton<IUserContext>(user)
      .AddSingleton<PlatformAppService>(fullApp)
      .AddTransient<TestOrderMasterGridService>()
      .BuildServiceProvider();

    var idemKey = $"idem-key-wave4-{Guid.NewGuid():N}";
    var gridPath = ServicePathResolver.Resolve(typeof(TestOrderMasterGridService));
    var body1 = JsonSerializer.Serialize(new
    {
      selectedIds = new[] { order.Id },
      selectedVersions = new[] { order.Ver },
      tagId = tag.Id,
      note = "Once"
    });

    async Task<(int Status, string Body)> PostActionAsync(string jsonBody, string key)
    {
      var ctx = new DefaultHttpContext { RequestServices = services };
      ctx.Request.Path = $"/api/{gridPath}/stamp-note";
      ctx.Request.ContentType = "application/json";
      ctx.Request.Headers["Idempotency-Key"] = key;
      ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonBody));
      ctx.Response.Body = new MemoryStream();
      var gridInstance = services.GetRequiredService<TestOrderMasterGridService>();
      var res = await EndpointExecutor.DispatchAsync(
        typeof(TestOrderMasterGridService),
        gridInstance,
        "stamp-note",
        TestAppModule.OrderDocs,
        ctx.Request,
        services,
        CancellationToken.None);
      await res.ExecuteAsync(ctx);
      ctx.Response.Body.Position = 0;
      using var reader = new StreamReader(ctx.Response.Body);
      return (ctx.Response.StatusCode, await reader.ReadToEndAsync());
    }

    var first = await PostActionAsync(body1, idemKey);
    Assert.Equal(StatusCodes.Status200OK, first.Status);

    // Exact replay under same key returns 200 OK without executing StampNote a second time
    var replay = await PostActionAsync(body1, idemKey);
    Assert.Equal(StatusCodes.Status200OK, replay.Status);
    var dbOrder = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == order.Id);
    Assert.Equal("ORD-IDEM-FAST-Once", dbOrder.Number);

    // Reusing the same Idempotency-Key with a mutated payload returns 409 Conflict
    var mutatedBody = JsonSerializer.Serialize(new
    {
      selectedIds = new[] { order.Id },
      selectedVersions = new[] { order.Ver },
      tagId = tag.Id,
      note = "Tampered"
    });
    var conflict = await PostActionAsync(mutatedBody, idemKey);
    Assert.Equal(StatusCodes.Status409Conflict, conflict.Status);
  }

  [Fact]
  public async Task AuditRegressions_ParentAffectedViewGate_ExpressionChildRuleDedup_AndExactGreaterThan()
  {
    await using var db = CreateDbContext();

    // 1. Expression-bodied ChildRule property read multiple times runs follow effects only once
    var events = new List<string>();
    var orderFlow = new TestOrderFlowService(db, events);
    _ = orderFlow.Statuses;
    _ = orderFlow.Lines;
    _ = orderFlow.Lines;

    var ord = new TestInvoice { Number = "ORD-DEDUP", Status = 1 };
    db.Invoices.Add(ord);
    await db.SaveChangesAsync();
    var line = new TestInvoiceLine { InvoiceId = ord.Id, Title = "Bolt", Amount = 10m, TierCode = 2 };
    db.InvoiceLines.Add(line);
    await db.SaveChangesAsync();

    var mutCtx = new GridBatchContext<TestInvoice>([ord.Id], [ord], ct => db.SaveChangesAsync(ct), db);
    await orderFlow.FollowMovesAsync(mutCtx, new HashSet<long> { ord.Id }, CancellationToken.None);
    Assert.Equal(1, events.Count(e => e == "FollowEffect:Lines"));

    // 2. ParentScopeValidator omits AffectedDto when caller lacks View permission on parent Feature, while DB total still updates
    var noParentViewApp = new TestPlatformAppService(db, new FixedUser(77, "Test.DirectLines.Edit"));
    var hiddenParent = new TestInvoice { Number = "ORD-HIDDEN", Status = 1, AuditedTotal = 0m };
    db.Invoices.Add(hiddenParent);
    await db.SaveChangesAsync();
    db.InvoiceLines.Add(new TestInvoiceLine { InvoiceId = hiddenParent.Id, Title = "SecretLine", Amount = 75m, TierCode = 1 });
    await db.SaveChangesAsync();

    var masterFormPath = ServicePathResolver.Resolve(typeof(TestOrderMasterFormService));
    var affectedDenied = await Platform.App.Internals.ParentScopeValidator.RecalculateBoundParentsAsync(
      db,
      noParentViewApp,
      masterFormPath,
      masterFormPath,
      typeof(TestOrderMasterFormService),
      [hiddenParent.Id],
      CancellationToken.None,
      hiddenParent.Id);
    Assert.Null(affectedDenied);
    var updatedHidden = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == hiddenParent.Id);
    Assert.Equal(75m, updatedHidden.AuditedTotal);

    // 3. GreaterThan(0) is exact (ExclusiveMin = true): accepts 0.00005m and rejects 0m
    var ask = TransitionAsk.Of<TestInvoiceLine>(e => e.Add(x => x.Amount).GreaterThan(0));
    var tinyValid = await ask.ValidateAsync(new TestInvoiceLine { Amount = 0.00005m }, db, null, Localizer.Instance, CancellationToken.None);
    Assert.Empty(tinyValid);

    var zeroInvalid = await ask.ValidateAsync(new TestInvoiceLine { Amount = 0m }, db, null, Localizer.Instance, CancellationToken.None);
    Assert.Contains(zeroInvalid, m => m.Path == "amount");
  }
}
