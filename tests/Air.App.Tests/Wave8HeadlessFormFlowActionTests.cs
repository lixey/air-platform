using DemoEcom;
using DemoEcom.CustomerOrder;
using DemoEcom.Data;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class Wave8HeadlessFormFlowActionTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public Wave8HeadlessFormFlowActionTests()
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
  public async Task DescribeFlow_AndPreviewMove_EnforceRbacWhenGuardsAndAskDefaultsImmediately()
  {
    await using var db = CreateDbContext();
    var fullApp = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var noApproveApp = new TestPlatformAppService(
      db,
      new FixedUser(11, TestAppModule.Wave8Orders.View, TestAppModule.Wave8Orders.Edit));

    var tag = new TestTag { Name = "DuplicateOrder", IsActive = true };
    var unprinted = new TestInvoice { Number = "W8-UNPRINTED", Status = 1, PrintedCount = 0 };
    var onHold = new TestInvoice { Number = "W8-HOLD", Status = 1, PrintedCount = 2, InternalMemo = "HOLD" };
    var ready = new TestInvoice { Number = "W8-READY", Status = 1, PrintedCount = 2 };
    db.Tags.Add(tag);
    db.Invoices.AddRange(unprinted, onHold, ready);
    await db.SaveChangesAsync();

    var form = new TestWave8OrderFormService(db, fullApp);
    var flowDesc = await form.DescribeStatusFlowAsync();
    Assert.NotNull(flowDesc);
    Assert.Equal("status", flowDesc.StatusPath);
    Assert.Equal(3, flowDesc.Statuses.Count);
    Assert.Contains(flowDesc.Transitions, t => t.Name == "Approve" && t.AskElements.Count == 0);
    Assert.Contains(flowDesc.Transitions, t => t.Name == "Reject" && t.AskElements.Count == 2);

    // 1. RBAC permission check fails immediately during preview
    var restrictedForm = new TestWave8OrderFormService(db, noApproveApp);
    await Assert.ThrowsAsync<FormRefusedException>(() => restrictedForm.PreviewMoveStatusAsync(ready.Id, "Approve"));

    // 2. Synchronous .When(...) predicate fails immediately with custom reason
    var whenEx = await Assert.ThrowsAsync<FormRefusedException>(() => form.PreviewMoveStatusAsync(unprinted.Id, "Approve"));
    Assert.Contains(whenEx.Messages, m => (m.Detail ?? string.Empty).Contains("printed", StringComparison.OrdinalIgnoreCase));

    // 3. Async .Guard(...) fails immediately before user confirmation
    var guardEx = await Assert.ThrowsAsync<FormRefusedException>(() => form.PreviewMoveStatusAsync(onHold.Id, "Approve"));
    Assert.Contains(guardEx.Messages, m => (m.Detail ?? string.Empty).Contains("credit hold", StringComparison.OrdinalIgnoreCase));

    // 4. Valid Approve transition succeeds in preview with current/target status labels
    var okApprove = await form.PreviewMoveStatusAsync(ready.Id, "Approve");
    Assert.Equal("Approve", okApprove.TransitionName);
    Assert.Equal("Draft", okApprove.CurrentStatusName);
    Assert.Equal("Approved", okApprove.TargetStatusName);

    // 5. Reject .Ask<TInput> validates required fields and binds AskValues cleanly
    var missingTagEx = await Assert.ThrowsAsync<FormRefusedException>(() => form.PreviewMoveStatusAsync(ready.Id, "Reject"));
    Assert.Contains(missingTagEx.Messages, m => m.Path == "tagId");

    var validAsk = JsonSerializer.SerializeToElement(new { tagId = tag.Id, note = "AutoReject:W8-READY" });
    var validReject = await form.PreviewMoveStatusAsync(ready.Id, "Reject", validAsk);
    Assert.Equal("AutoReject:W8-READY", validReject.AskValues["note"]?.ToString());
  }

  [Fact]
  public async Task PrepareCompositeDraft_SaveRecord_AndApplyChildRows_ComputeOnePassSumTo_HideSensitive_AndRecalculateOnce()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var vipTag = new TestTag { Name = "VIP", IsActive = true };
    db.Tags.Add(vipTag);
    await db.SaveChangesAsync();

    var form = new TestWave8OrderFormService(db, app);

    // 1. [Sensitive] property is automatically marked IsSecret and hidden from GetElementsMetadataAsync
    var (headlessElements, _) = await form.GetElementsMetadataAsync();
    Assert.DoesNotContain(headlessElements, e => string.Equals(e.Path, "apiSecret", StringComparison.OrdinalIgnoreCase));

    // 2. Blocking SetRules() error and child validation errors surface in composite draft Messages
    var blockedArgs = JsonSerializer.SerializeToElement(new
    {
      values = new { number = "BLOCKED" },
      lines = new[] { new { title = "BadLine", amount = -5m } }
    });
    var blockedDraft = await form.PrepareCompositeDraftAsync(new FormCompositeDraftRequest
    {
      Args = blockedArgs,
      Values = blockedArgs.GetProperty("values")
    });
    Assert.Contains(blockedDraft.Messages, m => m.Type == MessageType.Error && (m.Detail ?? string.Empty).Contains("Blocked order", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(blockedDraft.Messages, m => m.Type == MessageType.Error && (m.Detail ?? string.Empty).Contains("negative", StringComparison.OrdinalIgnoreCase));

    // 3. Create composite draft resolves child reference by text ("VIP"), rolls up 1-pass SumTo, and keeps .AsWarning() non-blocking
    var createArgs = JsonSerializer.SerializeToElement(new
    {
      values = new { number = "W8-100" },
      lines = new object[]
      {
        new { title = "Engine", amount = 600m, tagId = "VIP" },
        new { title = "Gearbox", amount = 400m }
      }
    });
    var createDraft = await form.PrepareCompositeDraftAsync(new FormCompositeDraftRequest
    {
      Args = createArgs,
      Values = createArgs.GetProperty("values")
    });
    Assert.DoesNotContain(createDraft.Messages, m => m.Type == MessageType.Error);
    Assert.Contains(createDraft.Messages, m => m.Type == MessageType.Warning && (m.Detail ?? string.Empty).Contains("High single-line", StringComparison.OrdinalIgnoreCase));
    Assert.Equal(1_000m, Convert.ToDecimal(createDraft.Values["cachedLineTotal"]));

    var savePayload = JsonSerializer.SerializeToElement(createDraft.Values);
    var savedHeader = await form.SaveRecordAsync(new FormRequest { Value = savePayload });
    Assert.Equal(MutationOutcome.Committed, savedHeader.Outcome);
    Assert.True(savedHeader.Id > 0);

    var lineFormPath = ServicePathResolver.Resolve<TestWave8OrderLineFormService>();
    var lineGridPath = ServicePathResolver.Resolve<TestWave8OrderLineGridService>();
    var initialBatch = createDraft.Children.Select(g => new FormChildBatchGroupDto
    {
      ChildKey = g.ChildKey,
      ChildGridService = lineGridPath,
      ChildFormService = lineFormPath,
      Mode = g.Mode,
      HasUpdate = g.HasUpdate,
      Rows = g.Rows.Select(r => new Dictionary<string, object?>(r.Values, StringComparer.OrdinalIgnoreCase)).ToList()
    }).ToList();

    await form.ApplyChildRowsAsync(savedHeader.Id, isParentEdit: false, createDraft.Values, initialBatch);

    var afterInitial = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == savedHeader.Id);
    Assert.Equal(1_000m, afterInitial.CachedLineTotal);
    Assert.Equal(1_000m, afterInitial.AuditedTotal);
    // BoundParentRecalculator ran ONCE for the entire 2-row child batch (not 2 times)
    Assert.Equal(1, afterInitial.PrintedCount);

    // 4. Edit with mode="replace" computes exact new sum (1200, NOT 1000 + 1200 = 2200!) and surfaces header .AsWarning()
    var editForm = new TestWave8OrderFormService(db, app);
    var replaceArgs = JsonSerializer.SerializeToElement(new
    {
      id = afterInitial.Id,
      values = new { number = "W8-100" },
      lines = new
      {
        mode = "replace",
        rows = new[]
        {
          new { title = "A", amount = 300m },
          new { title = "B", amount = 300m },
          new { title = "C", amount = 300m },
          new { title = "D", amount = 300m }
        }
      }
    });
    var replaceDraft = await editForm.PrepareCompositeDraftAsync(new FormCompositeDraftRequest
    {
      RecordId = afterInitial.Id,
      Args = replaceArgs,
      Values = replaceArgs.GetProperty("values")
    });
    Assert.Equal(1_200m, Convert.ToDecimal(replaceDraft.Values["cachedLineTotal"]));
    Assert.Contains(replaceDraft.Messages, m => m.Type == MessageType.Warning && (m.Detail ?? string.Empty).Contains("Large order total", StringComparison.OrdinalIgnoreCase));

    var replaceHeaderJson = JsonSerializer.SerializeToElement(replaceDraft.Values);
    var savedReplace = await new TestWave8OrderFormService(db, app).SaveRecordAsync(new FormRequest
    {
      Id = afterInitial.Id,
      Ver = afterInitial.Ver,
      Value = replaceHeaderJson
    });
    Assert.Equal(MutationOutcome.Committed, savedReplace.Outcome);
    Assert.Contains(savedReplace.Warnings, m => m.Type == MessageType.Warning);

    var replaceBatch = replaceDraft.Children.Select(g => new FormChildBatchGroupDto
    {
      ChildKey = g.ChildKey,
      ChildGridService = lineGridPath,
      ChildFormService = lineFormPath,
      Mode = g.Mode,
      HasUpdate = g.HasUpdate,
      Rows = g.Rows.Select(r => new Dictionary<string, object?>(r.Values, StringComparer.OrdinalIgnoreCase)).ToList()
    }).ToList();

    await new TestWave8OrderFormService(db, app).ApplyChildRowsAsync(afterInitial.Id, isParentEdit: true, replaceDraft.Values, replaceBatch);

    var afterReplace = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == afterInitial.Id);
    Assert.Equal(1_200m, afterReplace.CachedLineTotal);
    Assert.Equal(1_200m, afterReplace.AuditedTotal);
    // Deleting 2 old lines + inserting 4 new lines triggered RecalculateAsync exactly ONCE more (1 -> 2, not 7!)
    Assert.Equal(2, afterReplace.PrintedCount);

    // 5. Edit with mode="append" adds appended lines onto persisted lines (1200 + 150 = 1350)
    var appendArgs = JsonSerializer.SerializeToElement(new
    {
      id = afterInitial.Id,
      lines = new { mode = "append", rows = new[] { new { title = "E", amount = 150m } } }
    });
    var appendDraft = await new TestWave8OrderFormService(db, app).PrepareCompositeDraftAsync(new FormCompositeDraftRequest
    {
      RecordId = afterInitial.Id,
      Args = appendArgs
    });
    Assert.Equal(1_350m, Convert.ToDecimal(appendDraft.Values["cachedLineTotal"]));

    // 6. Edit with mode="replace" and empty rows resets SumTo header to 0m
    var clearArgs = JsonSerializer.SerializeToElement(new
    {
      id = afterInitial.Id,
      lines = new { mode = "replace", rows = Array.Empty<object>() }
    });
    var clearDraft = await new TestWave8OrderFormService(db, app).PrepareCompositeDraftAsync(new FormCompositeDraftRequest
    {
      RecordId = afterInitial.Id,
      Args = clearArgs
    });
    Assert.Equal(0m, Convert.ToDecimal(clearDraft.Values["cachedLineTotal"]));
  }

  [Fact]
  public async Task DescribeDeclaredActions_AndPreviewDeclaredAction_EnforceAllowAgentOptInAndWhenPredicates()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var tag = new TestTag { Name = "Express", IsActive = true };
    var draftOrder = new TestInvoice { Number = "W8-ACT-1", Status = 1 };
    var approvedOrder = new TestInvoice { Number = "W8-ACT-2", Status = 2 };
    db.Tags.Add(tag);
    db.Invoices.AddRange(draftOrder, approvedOrder);
    await db.SaveChangesAsync();

    var form = new TestWave8OrderFormService(db, app);
    var grid = new TestWave8OrderGridService(db, app);

    var agentFormActions = await form.DescribeDeclaredActionsAsync(automationOnly: true);
    var singleFormAction = Assert.Single(agentFormActions);
    Assert.Equal("StampAgent", singleFormAction.Handle);
    Assert.Equal(2, singleFormAction.AskElements.Count);

    var agentGridActions = await grid.DescribeDeclaredActionsAsync(automationOnly: true);
    Assert.Equal(
      ["GlobalAgentPing", "StampAgent"],
      agentGridActions.Select(a => a.Handle).OrderBy(x => x, StringComparer.Ordinal).ToArray());

    // UI-only action without .AllowAgent() is refused fail-closed when requireAutomationOptIn: true
    await Assert.ThrowsAsync<FormRefusedException>(() =>
      form.PreviewDeclaredActionAsync(
        "UiOnlyStamp",
        [draftOrder.Id],
        requireAutomationOptIn: true));

    // .When(i => i.Status == 1) fails immediately on approved order
    var deniedWhenEx = await Assert.ThrowsAsync<FormRefusedException>(() =>
      form.PreviewDeclaredActionAsync(
        "StampAgent",
        [approvedOrder.Id],
        JsonSerializer.SerializeToElement(new { tagId = tag.Id, note = "Try" }),
        requireAutomationOptIn: true));
    Assert.Contains(deniedWhenEx.Messages, m => (m.Detail ?? string.Empty).Contains("Only draft orders", StringComparison.OrdinalIgnoreCase));

    // Valid preview resolves Default(...) note and validates Ask<TInput> reference
    var validPreview = await form.PreviewDeclaredActionAsync(
      "StampAgent",
      [draftOrder.Id],
      JsonSerializer.SerializeToElement(new { tagId = tag.Id }),
      requireAutomationOptIn: true);
    Assert.NotNull(validPreview);
    Assert.Equal("Stamp:W8-ACT-1", validPreview.AskValues["note"]?.ToString());

    // DemoEcom follow-up actions opt into .AllowAgent() declaratively
    ModuleCatalog.Instance.Register(new EcomModule());
    var demoTenant = new FixedTenant(1, 10, new HashSet<string>(["*"]));
    var demoUser = new FixedUser(10, "*");
    var demoDbOptions = new DbContextOptionsBuilder<EcomDbContext>().UseSqlite(_connection).Options;
    await using var demoDb = new EcomDbContext(demoDbOptions, demoTenant);
    var demoApp = new EcomService(demoUser, new EmptyServiceProvider(), demoTenant, Localizer.Instance);
    var demoOrderGrid = new EcomCustomerOrderGridService(demoDb, demoApp);
    var demoActions = await demoOrderGrid.DescribeDeclaredActionsAsync(automationOnly: true);
    Assert.Equal(
      ["CreateIncomingPayment", "CreateSalesInvoice"],
      demoActions.Select(a => a.Handle).OrderBy(x => x, StringComparer.Ordinal).ToArray());
  }
}
