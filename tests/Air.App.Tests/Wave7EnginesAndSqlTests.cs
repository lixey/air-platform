using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Platform.App.Internals;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class Wave7EnginesAndSqlTests : IDisposable
{
  private readonly SqliteConnection _connection;
  private readonly SqlCountCommandInterceptor _sqlInterceptor = new();

  public Wave7EnginesAndSqlTests()
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
      .AddInterceptors(_sqlInterceptor)
      .Options;
    var effectiveRights = rights.Length > 0
      ? new HashSet<string>(rights, StringComparer.OrdinalIgnoreCase)
      : new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase);
    var db = new AppTestDbContext(options, new FixedTenant(tenantId, userId, effectiveRights));
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public async Task ParentFilterExpressions_NullableLongForeignKey_EmitsSargableInWithoutCoalesce_AndNeverLeaksNullRowsForZero()
  {
    await using var db = CreateDbContext();
    db.AppFiles.AddRange(
      new TestAppFile { Name = "orphan.txt", ContentType = "text/plain", StorageKey = FileKeys.New(), Sha256 = "00", InvoiceId = null },
      new TestAppFile { Name = "inv10.txt", ContentType = "text/plain", StorageKey = FileKeys.New(), Sha256 = "10", InvoiceId = 10 },
      new TestAppFile { Name = "inv20.txt", ContentType = "text/plain", StorageKey = FileKeys.New(), Sha256 = "20", InvoiceId = 20 });
    await db.SaveChangesAsync();

    var fkProp = typeof(TestAppFile).GetProperty(nameof(TestAppFile.InvoiceId))!;

    var zeroPredicate = ParentFilterExpressions.BuildKeyInFilter<TestAppFile>(fkProp, [0L]);
    var zeroQuery = db.AppFiles.AsNoTracking().Where(zeroPredicate);
    var zeroSql = zeroQuery.ToQueryString();
    Assert.DoesNotContain("COALESCE", zeroSql, StringComparison.OrdinalIgnoreCase);
    Assert.Empty(await zeroQuery.ToListAsync());

    var validPredicate = ParentFilterExpressions.BuildKeyInFilter<TestAppFile>(fkProp, [10L, 20L]);
    var validQuery = db.AppFiles.AsNoTracking().Where(validPredicate);
    var validSql = validQuery.ToQueryString();
    Assert.DoesNotContain("COALESCE", validSql, StringComparison.OrdinalIgnoreCase);
    var matched = await validQuery.OrderBy(f => f.InvoiceId).ToListAsync();
    Assert.Equal([10L, 20L], matched.Select(f => f.InvoiceId!.Value).ToArray());
  }

  [Fact]
  public async Task NamedFilters_BatchSingleQueryCounts_EnforcePascalCaseKeys_AndMatchCaseInsensitively()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    db.Invoices.AddRange(
      new TestInvoice { Number = "INV-1", Status = 1, AuditedTotal = 40m },
      new TestInvoice { Number = "INV-2", Status = 1, AuditedTotal = 140m },
      new TestInvoice { Number = "INV-3", Status = 2, AuditedTotal = 240m },
      new TestInvoice { Number = "INV-HIDDEN", Status = 1, AuditedTotal = 900m, IsHidden = true });
    await db.SaveChangesAsync();

    var grid = new TestCountedInvoiceGridService(db, app);
    _sqlInterceptor.Reset();

    var page = await grid.GetDataAsync(
      new GridRequest { IsDataOnly = true, NamedFilters = ["stage.closed", "HIGHVALUE"] },
      CancellationToken.None);

    Assert.Single(page.Rows);
    Assert.NotNull(page.NamedFilterCounts);
    Assert.Equal(1, page.NamedFilterCounts!["Stage.Open"]);
    Assert.Equal(1, page.NamedFilterCounts["Stage.Closed"]);
    Assert.Equal(2, page.NamedFilterCounts["Stage.All"]);
    Assert.Equal(1, page.NamedFilterCounts["HighValue"]);

    // 1 query for page rows + 1 query for total count + 1 batched conditional-aggregation query for NamedFilterCounts!
    Assert.True(
      _sqlInterceptor.Commands.Count <= 3,
      $"Expected <= 3 SQL commands for page + batched NamedFilterCounts, got {_sqlInterceptor.Commands.Count}: {string.Join(" || ", _sqlInterceptor.Commands)}");

    var invalidQuery = new GridQuery<TestInvoice, TestInvoice>();
    Assert.Throws<InvalidOperationException>(() => invalidQuery.NamedFilter("lowercaseKey", "Bad", x => x.Status == 1));
    Assert.Throws<InvalidOperationException>(() => invalidQuery.NamedFilter("Stage", "Stage").Item("bad_item", "Bad", x => x.Status == 1));
  }

  [Fact]
  public async Task UnifiedRowShaper_PreservesRowFlagsColorsRestrictionsAndConfirmEdit_AcrossGetDataAndExport()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    db.Invoices.AddRange(
      new TestInvoice { Number = "INV-LOW", AuditedTotal = 50m },
      new TestInvoice { Number = "INV-VIP", AuditedTotal = 250m },
      new TestInvoice { Number = "INV-SECRET", AuditedTotal = 999m, IsHidden = true });
    await db.SaveChangesAsync();

    var grid = new TestRestrictedEnrichedGridService(db, app);
    var screen = await grid.BuildScreenAsync("test/invoices", new GridRequest(), CancellationToken.None);
    var gridModel = FindGridModel(screen.Layout!.Root);
    var numberCol = Assert.Single(gridModel.Columns, c => c.Path == "number");
    Assert.True(numberCol.ConfirmsEdit);
    Assert.Equal("Confirm invoice number change?", numberCol.ConfirmText);

    // Calling BuildScreenAsync again must keep Columns.Items pure (no in-place mutation)
    var secondScreen = await grid.BuildScreenAsync("test/invoices", new GridRequest(), CancellationToken.None);
    var secondGridModel = FindGridModel(secondScreen.Layout!.Root);
    Assert.Equal(numberCol.Label, Assert.Single(secondGridModel.Columns, c => c.Path == "number").Label);

    var page = await grid.GetDataAsync(new GridRequest(), CancellationToken.None);
    Assert.Equal(2, page.Rows.Count);
    Assert.Equal("INV-LOW/2", page.Rows[0].Cells["computedLabel"]);
    Assert.Equal(UiColor.Slate, page.Rows[0].Colors!["number"]);
    Assert.Null(page.Rows[0].Flags);

    Assert.Equal("INV-VIP/2", page.Rows[1].Cells["computedLabel"]);
    Assert.Equal(UiColor.Emerald, page.Rows[1].Colors!["number"]);
    Assert.Equal(["vip"], page.Rows[1].Flags);

    grid.ShapedRowsSeen.Clear();
    var exportResult = await grid.ExportAsync(new GridRequest(), CancellationToken.None);
    Assert.NotNull(exportResult);
    Assert.Equal(2, grid.ShapedRowsSeen.Count);
    Assert.Null(grid.ShapedRowsSeen[0].Flags);
    Assert.Equal(["vip"], grid.ShapedRowsSeen[1].Flags);
    Assert.Equal("INV-VIP/2", grid.ShapedRowsSeen[1].Cells["computedLabel"]);
  }

  [Fact]
  public async Task ScreenSetPanels_AndReportTablesUnification_EnforceStartupAndModuleIsolationLaws()
  {
    await using var db = CreateDbContext();
    var fullApp = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var limitedApp = new TestPlatformAppService(db, new FixedUser(10, TestAppModule.SummaryReport.View.Key));

    // 1. Async SetPanelsAsync preserves servicePath as root layout key and builds panels
    var asyncScreenSvc = new TestAsyncWorkspaceScreenService(fullApp);
    var screenDto = await asyncScreenSvc.BuildScreenAsync("test/composite-workspace", CancellationToken.None);
    Assert.Equal("test/composite-workspace", screenDto.Layout!.Key);
    Assert.Equal(2, screenDto.Layout.Root.Children!.Count);

    // 2. Overriding both SetPanels and SetPanelsAsync fails startup validation
    IPlatformService dualScreen = new MockBadDualScreenService(fullApp);
    Assert.Throws<InvalidOperationException>(() => dualScreen.ValidateConfiguration(ModuleCatalog.Instance));

    // 3. ReportDataDto.Table is a [JsonIgnore] convenience alias over Tables
    var singleTable = new ReportTableDto { Key = "summary", Title = "Summary" };
    var reportData = new ReportDataDto { Table = singleTable };
    Assert.Single(reportData.Tables!);
    Assert.Same(singleTable, reportData.Table);
    var serialized = JsonSerializer.Serialize(reportData, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    using var parsedJson = JsonDocument.Parse(serialized);
    Assert.False(parsedJson.RootElement.TryGetProperty("table", out _));
    Assert.True(parsedJson.RootElement.TryGetProperty("tables", out var tablesElem));
    Assert.Equal(1, tablesElem.GetArrayLength());

    // 4. ReportTable.OpenWith enforces ADR #8 vertical-only module isolation and RBAC CanView
    var reportHost = new TestSummaryReportService(fullApp);
    var sampleRows = new[] { (Id: 42L, Code: "DOC-42") };
    Assert.Throws<InvalidOperationException>(() =>
      ReportTable.For(sampleRows, reportHost).OpenWith<MockParallelScreenGridService>(r => r.Id));

    var allowedTable = ReportTable.For(sampleRows, reportHost)
      .Key("docs")
      .Text("code", "Code", r => r.Code)
      .OpenWith<TestInvoiceFormService>(r => r.Id)
      .Build();
    Assert.NotNull(allowedTable.Rows[0].Open);
    Assert.Equal(42L, allowedTable.Rows[0].Open!.Id);

    var restrictedReportHost = new TestSummaryReportService(limitedApp);
    var strippedTable = ReportTable.For(sampleRows, restrictedReportHost)
      .Key("docs")
      .Text("code", "Code", r => r.Code)
      .OpenWith<TestInvoiceFormService>(r => r.Id)
      .Build();
    Assert.Null(strippedTable.Rows[0].Open);
  }

  [Fact]
  public async Task DeleteFilesHook_KeepsPhysicalBlobsOnRollback_DeletesUnreferencedBlobsOnCommit_AndClampsPre1980ZipDates()
  {
    await using var db = CreateDbContext();
    var tempDir = Path.Combine(Path.GetTempPath(), $"platform-w7-files-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
      var storage = new LocalFileStorage(tempDir);
      var services = new ServiceCollection()
        .AddLogging()
        .AddSingleton<IFileStorage>(storage)
        .AddSingleton<IFileStores>(new DefaultFileStores(storage))
        .AddSingleton<FileLinks>()
        .BuildServiceProvider();

      var app = new TestPlatformAppService(db, new FixedUser(10, "*"), services);

      var inv1 = new TestInvoice { Number = "INV-FILE-1", Status = 10 };
      var inv2 = new TestInvoice { Number = "INV-FILE-2", Status = 10 };
      db.Invoices.AddRange(inv1, inv2);
      await db.SaveChangesAsync();

      var exclusiveKey = FileKeys.New();
      var exclusiveThumbKey = FileKeys.New();
      var sharedKey = FileKeys.New();
      await storage.WriteAsync(exclusiveKey, new MemoryStream("exclusive-body"u8.ToArray()));
      await storage.WriteAsync(exclusiveThumbKey, new MemoryStream("exclusive-thumb"u8.ToArray()));
      await storage.WriteAsync(sharedKey, new MemoryStream("shared-body"u8.ToArray()));

      var vintageFile = new TestAppFile
      {
        InvoiceId = inv1.Id,
        Name = "vintage.txt",
        ContentType = "text/plain",
        Size = 14,
        StorageKey = exclusiveKey,
        ThumbnailKey = exclusiveThumbKey,
        Sha256 = "aa11",
        CreatedAt = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)
      };
      var sharedFile1 = new TestAppFile
      {
        InvoiceId = inv1.Id,
        Name = "shared-1.txt",
        ContentType = "text/plain",
        Size = 11,
        StorageKey = sharedKey,
        Sha256 = "bb22",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
      };
      db.AppFiles.AddRange(
        vintageFile,
        sharedFile1,
        new TestAppFile
        {
          InvoiceId = inv2.Id,
          Name = "shared-2.txt",
          ContentType = "text/plain",
          Size = 11,
          StorageKey = sharedKey,
          Sha256 = "bb22",
          CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
      await db.SaveChangesAsync();

      // Verify DownloadAll clamps pre-1980 CreatedAt without throwing ArgumentOutOfRangeException
      var fileGrid = new TestAppFileGridService(db, app);
      var zipResult = await fileGrid.DownloadAll(new GridRequest { ParentId = inv1.Id }, CancellationToken.None);
      var httpCtx = new DefaultHttpContext { RequestServices = services };
      await using var zipBody = new MemoryStream();
      httpCtx.Response.Body = zipBody;
      await zipResult.ExecuteAsync(httpCtx);
      zipBody.Position = 0;
      using (var zip = new ZipArchive(zipBody, ZipArchiveMode.Read, leaveOpen: true))
      {
        var vintageEntry = Assert.Single(zip.Entries, e => e.Name == "vintage.txt");
        Assert.True(vintageEntry.LastWriteTime.Year >= 1980);
      }

      // Single-target file actions prefer request.Id over SelectedIds[0] when both are sent
      var renameRes = await fileGrid.SetInfo(
        new FileInfoRequest { ParentId = inv1.Id, Id = vintageFile.Id, SelectedIds = [sharedFile1.Id], Name = "vintage-renamed" },
        CancellationToken.None);
      Assert.Equal(MutationOutcome.Committed, renameRes.Outcome);
      Assert.Equal("vintage-renamed.txt", (await db.AppFiles.AsNoTracking().SingleAsync(f => f.Id == vintageFile.Id)).Name);
      Assert.Equal("shared-1.txt", (await db.AppFiles.AsNoTracking().SingleAsync(f => f.Id == sharedFile1.Id)).Name);

      // 1. Rollback in AfterDelete -> DB rows AND physical blobs MUST remain intact!
      var rollbackGrid = new TestInvoiceFileDeleteGridService(db, app, refuseInAfterDelete: true);
      var refused = await rollbackGrid.DeleteAsync(new GridRequest { SelectedIds = [inv1.Id] }, CancellationToken.None);
      Assert.Equal(MutationOutcome.Refused, refused.Outcome);
      Assert.True(await db.Invoices.AsNoTracking().AnyAsync(i => i.Id == inv1.Id));
      Assert.Equal(2, await db.AppFiles.AsNoTracking().CountAsync(f => f.InvoiceId == inv1.Id));
      await using (var s1 = await storage.OpenReadAsync(exclusiveKey))
      {
        Assert.NotNull(s1);
      }

      await using (var s2 = await storage.OpenReadAsync(exclusiveThumbKey))
      {
        Assert.NotNull(s2);
      }

      // 2. Commit -> inv1 + its 2 TestAppFile rows deleted; exclusiveKey + exclusiveThumbKey deleted; sharedKey kept for inv2!
      db.ChangeTracker.Clear();
      var commitGrid = new TestInvoiceFileDeleteGridService(db, app, refuseInAfterDelete: false);
      var committed = await commitGrid.DeleteAsync(new GridRequest { SelectedIds = [inv1.Id] }, CancellationToken.None);
      Assert.Equal(MutationOutcome.Committed, committed.Outcome);
      Assert.Equal(1, committed.Deleted);
      Assert.False(await db.Invoices.AsNoTracking().AnyAsync(i => i.Id == inv1.Id));
      Assert.Equal(0, await db.AppFiles.AsNoTracking().CountAsync(f => f.InvoiceId == inv1.Id));
      Assert.Null(await storage.OpenReadAsync(exclusiveKey));
      Assert.Null(await storage.OpenReadAsync(exclusiveThumbKey));
      await using (var sharedStream = await storage.OpenReadAsync(sharedKey))
      {
        Assert.NotNull(sharedStream);
      }
    }
    finally
    {
      try
      {
        Directory.Delete(tempDir, recursive: true);
      }
      catch
      {
      }
    }
  }

  [Fact]
  public async Task ExecutionItems_IsolatePerExecutionScope_AndFormSinglePassLoadReusesTrackedEntity()
  {
    var execSvc = new TestExecutionScopeService();
    execSvc.StoreString("token", "alpha");
    execSvc.StoreTyped(new Version(7, 5));
    Assert.Equal("alpha", execSvc.ReadString("token"));
    Assert.Equal(new Version(7, 5), execSvc.ReadTyped<Version>());

    ((IPlatformService)execSvc).ResetExecutionScope();
    Assert.Null(execSvc.ReadString("token"));
    Assert.Null(execSvc.ReadTyped<Version>());

    await using var db = CreateDbContext();
    var inv = new TestInvoice { Number = "INV-SINGLE-PASS", Status = 1, AuditedTotal = 300m };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var form = new TestInvoiceFormService(db, app);

    _sqlInterceptor.Reset();
    var screen = await form.GetData(new FormRequest { Id = inv.Id }, new DefaultHttpContext().Request, CancellationToken.None);
    Assert.NotNull(screen.Data);

    var invoiceSelectCount = _sqlInterceptor.Commands.Count(sql =>
      sql.Contains("\"Invoices\"", StringComparison.OrdinalIgnoreCase) ||
      sql.Contains("test_invoice", StringComparison.OrdinalIgnoreCase));
    Assert.Equal(1, invoiceSelectCount);

    var saveCtx = new FormSaveContext<TestInvoice>(db) { Entity = inv };
    saveCtx.AddError(x => x.AuditedTotal, "Total exceeded limit");
    Assert.Equal("auditedTotal", Assert.Single(saveCtx.Errors).Path);

    var allowlistServices = new ServiceCollection()
      .AddSingleton<AppTestDbContext>(db)
      .AddSingleton<PlatformAppService>(app)
      .BuildServiceProvider();
    await ReferenceLookupAllowlist.EnsureWarmAsync(allowlistServices, CancellationToken.None);
    _sqlInterceptor.Reset();
    await ReferenceLookupAllowlist.EnsureWarmAsync(allowlistServices, CancellationToken.None);
    Assert.Empty(_sqlInterceptor.Commands);
  }

  private static GridModel FindGridModel(PanelNodeDto node)
  {
    if (node.Model is GridModel match)
    {
      return match;
    }

    if (node.Children is not null)
    {
      foreach (var child in node.Children)
      {
        if (child.Model is GridModel childMatch)
        {
          return childMatch;
        }
      }
    }

    throw new InvalidOperationException("GridModel not found in ScreenDto layout.");
  }
}
