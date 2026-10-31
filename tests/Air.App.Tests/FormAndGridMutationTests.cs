using Platform.App.Internals;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class FormAndGridMutationTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public FormAndGridMutationTests()
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
    var effectiveRights = rights.Length > 0 ? new HashSet<string>(rights, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase);
    var db = new AppTestDbContext(options, new FixedTenant(tenantId, userId, effectiveRights));
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public async Task ParentScope_PreloadsParentRecord_AndBlocksUnreachableOrCrossParentIdor()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    var reachableParent = new TestInvoice { Number = "INV-100", Status = 1, IsHidden = false };
    var hiddenParent = new TestInvoice { Number = "INV-HIDDEN", Status = 1, IsHidden = true };
    var otherParent = new TestInvoice { Number = "INV-200", Status = 1, IsHidden = false };
    db.Invoices.AddRange(reachableParent, hiddenParent, otherParent);
    await db.SaveChangesAsync();

    var invoiceFormPath = ServicePathResolver.Resolve(typeof(TestInvoiceFormService));

    var lineForm = new TestInvoiceLineFormService(db, app);
    var createJson = JsonSerializer.SerializeToElement(new { title = "Brake Pad", amount = 50m });
    var savedObj = await lineForm.SaveAsync(createJson, new FormRequest
    {
      ParentService = invoiceFormPath,
      ParentId = reachableParent.Id,
      Value = createJson
    }, CancellationToken.None);

    var savedLineEntity = Assert.IsType<TestInvoiceLine>(savedObj);
    var savedLine = await db.InvoiceLines.FirstAsync(l => l.Id == savedLineEntity.Id);
    Assert.Equal("INV-100:Brake Pad", savedLine.Title);

    var badParentForm = new TestInvoiceLineFormService(db, app);
    await Assert.ThrowsAsync<FormRefusedException>(async () => await badParentForm.SaveAsync(createJson, new FormRequest
    {
      ParentService = invoiceFormPath,
      ParentId = hiddenParent.Id,
      Value = createJson
    }, CancellationToken.None));

    var idorForm = new TestInvoiceLineFormService(db, app);
    var editJson = JsonSerializer.SerializeToElement(new { title = "Hacked", amount = 99m });
    await Assert.ThrowsAsync<FormRefusedException>(async () => await idorForm.SaveAsync(editJson, new FormRequest
    {
      Id = savedLine.Id,
      Ver = savedLine.Ver,
      ParentService = invoiceFormPath,
      ParentId = otherParent.Id,
      Value = editJson
    }, CancellationToken.None));

    var childGridNoParent = new TestInvoiceLineGridService(db, app);
    var noParentPage = await childGridNoParent.GetDataAsync(new GridRequest { IsDataOnly = true }, CancellationToken.None);
    Assert.Empty(noParentPage.Rows);

    var childGridWithParent = new TestInvoiceLineGridService(db, app);
    var parentPage = await childGridWithParent.GetDataAsync(new GridRequest { ParentId = reachableParent.Id, IsDataOnly = true }, CancellationToken.None);
    Assert.Single(parentPage.Rows);
  }

  [Fact]
  public async Task AsReferences_AndAttachPicked_RejectUnreachableTargetIds()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    var activeTag = new TestTag { Name = "Urgent", IsActive = true };
    var inactiveTag = new TestTag { Name = "ArchivedTag", IsActive = false };
    db.Tags.AddRange(activeTag, inactiveTag);
    await db.SaveChangesAsync();

    var invoiceForm = new TestInvoiceFormService(db, app);
    var badTagsJson = JsonSerializer.SerializeToElement(new
    {
      number = "INV-300",
      tagIds = new[] { activeTag.Id, inactiveTag.Id }
    });

    await Assert.ThrowsAsync<FormRefusedException>(async () => await invoiceForm.SaveAsync(badTagsJson, new FormRequest { Value = badTagsJson }, CancellationToken.None));

    var goodTagsJson = JsonSerializer.SerializeToElement(new
    {
      number = "INV-300",
      tagIds = new[] { activeTag.Id }
    });
    var saved = Assert.IsType<TestInvoice>(await invoiceForm.SaveAsync(goodTagsJson, new FormRequest { Value = goodTagsJson }, CancellationToken.None));
    Assert.True(saved.Id > 0);

    var invoiceFormPath = ServicePathResolver.Resolve(typeof(TestInvoiceFormService));
    var lineGrid = new TestInvoiceLineGridService(db, app);
    var pickRefused = await lineGrid.AttachTag(new GridPickRequest
    {
      ParentService = invoiceFormPath,
      ParentId = saved.Id,
      PickedId = inactiveTag.Id
    });
    Assert.Equal(MutationOutcome.Refused, pickRefused.Outcome);

    var pickCommitted = await lineGrid.AttachTag(new GridPickRequest
    {
      ParentService = invoiceFormPath,
      ParentId = saved.Id,
      PickedId = activeTag.Id
    });
    Assert.Equal(MutationOutcome.Committed, pickCommitted.Outcome);
  }

  [Fact]
  public async Task StatusFlow_SaveCell_AndChangeRestore_EnforceReadOnlyVersionAndKeepsVersion()
  {
    await using var db = CreateDbContext();
    var fullApp = new TestPlatformAppService(db, new FixedUser(10, "*"));

    var inv = new TestInvoice { Number = "INV-500", Status = 1, PrintedCount = 0 };
    using (db.BeginMutationRun())
    {
      db.Invoices.Add(inv);
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    using (db.BeginMutationRun())
    {
      inv.Number = "INV-500-EDITED";
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    var sc = new ServiceCollection();
    sc.AddSingleton<PlatformDbContext>(db);
    sc.AddSingleton(db);
    sc.AddSingleton<PlatformAppService>(fullApp);
    sc.AddTransient<TestInvoiceFormService>();
    var sp = sc.BuildServiceProvider();

    var grid = new TestInvoiceGridService(db, fullApp);
    var missingVerRes = await grid.SaveCellAsync(new GridCellRequest
    {
      Id = inv.Id,
      Ver = null,
      Path = "number",
      Value = JsonSerializer.SerializeToElement("INV-500-CELL")
    }, sp, CancellationToken.None);
    Assert.Equal(MutationOutcome.Conflict, missingVerRes.Outcome);

    var validCellRes = await grid.SaveCellAsync(new GridCellRequest
    {
      Id = inv.Id,
      Ver = inv.Ver,
      Path = "number",
      Value = JsonSerializer.SerializeToElement("INV-500-CELL")
    }, sp, CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, validCellRes.Outcome);

    var viewOnlyApp = new TestPlatformAppService(db, new FixedUser(10, "Test.Invoices.View"));
    var roForm = new TestInvoiceFormService(db, viewOnlyApp);
    var currentInv = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id);
    var roMove = await roForm.MoveStatusAsync(inv.Id, "Approve", null, currentInv.Ver, CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, roMove.Outcome);

    var changeRecord = await db.AppPlatformChange
      .OrderByDescending(c => c.Id)
      .FirstAsync(c => c.RowId == inv.Id && c.Operation == ChangeOperations.Modified);
    var changeGrid = new TestChangeGridService(db, fullApp);
    var restoreOk = await changeGrid.Restore(new GridRequest { Id = changeRecord.Id }, CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, restoreOk.Outcome);

    var reloadedInv = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id);
    Assert.Equal("INV-500-EDITED", reloadedInv.Number);

    using (db.BeginMutationRun())
    {
      var toEdit = await db.Invoices.FirstAsync(i => i.Id == inv.Id);
      toEdit.Number = "INV-500-EDITED-2";
      toEdit.PrintedCount = 5;
      await db.SaveChangesAsync();
      await db.FlushMutationJournalAsync();
    }

    var latestChangeRecord = await db.AppPlatformChange
      .OrderByDescending(c => c.Id)
      .FirstAsync(c => c.RowId == inv.Id && c.Operation == ChangeOperations.Modified);

    var restoreRefused = await changeGrid.Restore(new GridRequest { Id = latestChangeRecord.Id }, CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, restoreRefused.Outcome);
  }

  [Fact]
  public async Task OfferBatchEvaluator_EvaluatesMultipleOffersInSingleBatch()
  {
    await using var db = CreateDbContext();
    var inv1 = new TestInvoice { Number = "A", Status = 1 };
    var inv2 = new TestInvoice { Number = "B", Status = 2 };
    db.Invoices.AddRange(inv1, inv2);
    await db.SaveChangesAsync();

    var offers = new List<(string ActionKey, int ReasonIndex, Expression<Func<TestInvoice, bool>> Condition)>
    {
      ("Approve", 0, x => x.Status == 1),
      ("Archive", 0, x => x.Status == 2)
    };

    var map = await OfferBatchEvaluator.EvaluateAsync(
      db,
      [inv1.Id, inv2.Id],
      offers,
      CancellationToken.None);

    Assert.True(map.ContainsKey(inv1.Id));
    Assert.True(map[inv1.Id].ContainsKey("Archive"));
    Assert.False(map[inv1.Id].ContainsKey("Approve"));

    Assert.True(map.ContainsKey(inv2.Id));
    Assert.True(map[inv2.Id].ContainsKey("Approve"));
    Assert.False(map[inv2.Id].ContainsKey("Archive"));
  }

  [Fact]
  public async Task DisabledWhen_TransitionRequireRight_AndSingleQuerySum_AreEnforcedServerSide()
  {
    await using var db = CreateDbContext();
    var fullApp = new TestPlatformAppService(db, new FixedUser(10, "*"));

    var inv = new TestInvoice { Number = "INV-700", Status = 1 };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();

    db.InvoiceLines.AddRange(
      new TestInvoiceLine { InvoiceId = inv.Id, Title = "L1", Amount = 12.5m },
      new TestInvoiceLine { InvoiceId = inv.Id, Title = "L2", Amount = 27.5m });
    await db.SaveChangesAsync();

    var lineGrid = new TestInvoiceLineGridService(db, fullApp);
    var page = await lineGrid.GetDataAsync(new GridRequest { ParentId = inv.Id, IsDataOnly = true }, CancellationToken.None);
    Assert.NotNull(page.BottomRows);
    var sumRow = Assert.Single(page.BottomRows) as IDictionary<string, object?>;
    Assert.NotNull(sumRow);
    Assert.Equal(40.0m, Convert.ToDecimal(sumRow["amount"]));

    var editOnlyApp = new TestPlatformAppService(db, new FixedUser(10, "Test.Invoices.Edit"));
    var editOnlyForm = new TestInvoiceFormService(db, editOnlyApp);
    var moveDenied = await editOnlyForm.MoveStatusAsync(inv.Id, "Approve", null, inv.Ver, CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, moveDenied.Outcome);

    var fullForm = new TestInvoiceFormService(db, fullApp);
    var moveOk = await fullForm.MoveStatusAsync(inv.Id, "Approve", null, inv.Ver, CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, moveOk.Outcome);

    var approvedInv = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id);
    Assert.Equal(2, approvedInv.Status);

    var tamperNumberJson = JsonSerializer.SerializeToElement(new { number = "TAMPERED-WHEN-APPROVED" });
    await fullForm.SaveAsync(tamperNumberJson, new FormRequest
    {
      Id = approvedInv.Id,
      Ver = approvedInv.Ver,
      Value = tamperNumberJson
    }, CancellationToken.None);

    var afterTamper = await db.Invoices.AsNoTracking().FirstAsync(i => i.Id == inv.Id);
    Assert.Equal("INV-700", afterTamper.Number);
  }
}
