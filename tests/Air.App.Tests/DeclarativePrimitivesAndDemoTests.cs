using System.Globalization;
using DemoApp;
using DemoApp.Data;
using DemoEcom;
using DemoEcom.Data;
using DemoEcom.Resources;
using DemoEcom.SalesReport;
using DemoEcom.SalesWorkspace;
using DemoOrg;
using DemoOrg.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class DeclarativePrimitivesAndDemoTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public DeclarativePrimitivesAndDemoTests()
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

  private static TModel FindPanelModel<TModel>(PanelNodeDto node) where TModel : PanelModel
  {
    if (node.Model is TModel match)
    {
      return match;
    }

    if (node.Children is not null)
    {
      foreach (var child in node.Children)
      {
        try
        {
          return FindPanelModel<TModel>(child);
        }
        catch (InvalidOperationException)
        {
        }
      }
    }

    throw new InvalidOperationException($"Panel model '{typeof(TModel).Name}' not found.");
  }

  [Fact]
  public async Task NamedFilters_InlineAndShowCounts_ReturnLiveTabCounts_SupportItemOptOut_AndSkipPageTwoAndPicker()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    db.Invoices.AddRange(
      new TestInvoice { Number = "INV-1", Status = 1, AuditedTotal = 50m },
      new TestInvoice { Number = "INV-2", Status = 1, AuditedTotal = 150m },
      new TestInvoice { Number = "INV-3", Status = 1, AuditedTotal = 250m },
      new TestInvoice { Number = "INV-4", Status = 2, AuditedTotal = 500m },
      new TestInvoice { Number = "INV-HIDDEN", Status = 1, AuditedTotal = 999m, IsHidden = true });
    await db.SaveChangesAsync();

    var grid = new TestCountedInvoiceGridService(db, app);
    var screen = await grid.BuildScreenAsync("test/counted-invoice-grid", new GridRequest(), CancellationToken.None);
    var nav = FindPanelModel<GridModel>(screen.Layout!.Root).Nav;

    var navGroup = Assert.Single(
      nav.Elements.OfType<GridNavNamedFilterGroupModel>(),
      g => g.Key == "stage");
    Assert.True(navGroup.IsInline);

    var defaultPage = await grid.GetDataAsync(new GridRequest { IsDataOnly = true }, CancellationToken.None);
    Assert.Equal(3, defaultPage.Rows.Count);
    Assert.NotNull(defaultPage.NamedFilterCounts);
    Assert.Equal(3, defaultPage.NamedFilterCounts!["stage.open"]);
    Assert.Equal(1, defaultPage.NamedFilterCounts["stage.closed"]);
    Assert.Equal(4, defaultPage.NamedFilterCounts["stage.all"]);
    Assert.False(defaultPage.NamedFilterCounts.ContainsKey("stage.archived"));
    Assert.Equal(2, defaultPage.NamedFilterCounts["highValue"]);

    var allPage = await grid.GetDataAsync(
      new GridRequest { IsDataOnly = true, NamedFilters = ["stage.all"] },
      CancellationToken.None);
    Assert.Equal(4, allPage.Rows.Count);
    Assert.NotNull(allPage.NamedFilterCounts);
    Assert.Equal(3, allPage.NamedFilterCounts!["highValue"]);

    var secondPage = await grid.GetDataAsync(
      new GridRequest { IsDataOnly = true, Page = 2, PageSize = 2 },
      CancellationToken.None);
    Assert.Null(secondPage.NamedFilterCounts);

    var pickerPage = await grid.GetDataAsync(
      new GridRequest { IsDataOnly = true, IsReferencePicker = true },
      CancellationToken.None);
    Assert.Null(pickerPage.NamedFilterCounts);
  }

  [Fact]
  public async Task PreventDeleteWhenUsedBy_RefusesReferencedRecords_ForLongAndNullableLongForeignKeys()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    var usedInv = new TestInvoice { Number = "INV-USED", Status = 1 };
    var freeInv = new TestInvoice { Number = "INV-FREE", Status = 1 };
    var parentTag = new TestTag { Name = "Parent Category" };
    var freeTag = new TestTag { Name = "Unused Category" };
    db.Invoices.AddRange(usedInv, freeInv);
    db.Tags.AddRange(parentTag, freeTag);
    await db.SaveChangesAsync();

    var childTag = new TestTag { Name = "Child Tag", CategoryId = parentTag.Id };
    db.Tags.Add(childTag);
    db.InvoiceTags.Add(new TestInvoiceTag { InvoiceId = usedInv.Id, TagId = parentTag.Id });
    await db.SaveChangesAsync();

    var invGrid = new TestCountedInvoiceGridService(db, app);
    var refusedBatch = await invGrid.DeleteAsync(
      new GridRequest { SelectedIds = [usedInv.Id, freeInv.Id] },
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, refusedBatch.Outcome);
    Assert.Equal(0, refusedBatch.Deleted);
    var err = Assert.Single(refusedBatch.Messages!);
    Assert.Equal(usedInv.Id, err.RecordId);
    Assert.Contains("Invoice is referenced by tags.", err.Title ?? err.Detail ?? string.Empty, StringComparison.Ordinal);

    var committedFree = await invGrid.DeleteAsync(
      new GridRequest { SelectedIds = [freeInv.Id] },
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, committedFree.Outcome);
    Assert.Equal(1, committedFree.Deleted);

    var tagGrid = new TestCategoryUsedTagGridService(db, app);
    var refusedCategory = await tagGrid.DeleteAsync(
      new GridRequest { SelectedIds = [parentTag.Id] },
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Refused, refusedCategory.Outcome);
    Assert.Equal(0, refusedCategory.Deleted);
    Assert.Single(refusedCategory.Messages!, w => w.RecordId == parentTag.Id);

    var deletedFreeCategory = await tagGrid.DeleteAsync(
      new GridRequest { SelectedIds = [freeTag.Id] },
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, deletedFreeCategory.Outcome);
    Assert.Equal(1, deletedFreeCategory.Deleted);
  }

  [Fact]
  public async Task NonBlockingWarnings_AsWarning_RuleWarn_AndBeforeSaveWarn_CommitRecordAndReturnWarnings()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    var form1 = new TestWarnInvoiceFormService(db, app);
    var payload1 = JsonSerializer.SerializeToElement(new { number = "DRAFT-100", auditedTotal = 1500m });
    var saved1 = Assert.IsType<TestInvoice>(await form1.SaveAsync(
      payload1,
      new FormRequest { Value = payload1 },
      CancellationToken.None));
    Assert.True(saved1.Id > 0);

    var warnings1 = form1.GetLastSaveWarnings();
    Assert.NotNull(warnings1);
    Assert.Equal(2, warnings1.Count);
    Assert.All(warnings1, w => Assert.Equal(MessageType.Warning, w.Type));
    Assert.Contains(warnings1, w => w.Path == "auditedTotal");
    Assert.Contains(warnings1, w => w is { Code: "draft_prefix", Path: "number" });

    var form2 = new TestWarnInvoiceFormService(db, app);
    var payload2 = JsonSerializer.SerializeToElement(new { number = "INV-ZERO", auditedTotal = 0m });
    var saved2 = Assert.IsType<TestInvoice>(await form2.SaveAsync(
      payload2,
      new FormRequest { Value = payload2 },
      CancellationToken.None));
    Assert.True(saved2.Id > 0);
    var warn2 = Assert.Single(form2.GetLastSaveWarnings()!);
    Assert.Equal(MessageType.Warning, warn2.Type);
    Assert.Equal("zero_total", warn2.Code);
    Assert.Equal("auditedTotal", warn2.Path);
  }

  [Fact]
  public async Task CompositeScreen_PanelScreen_EmitsEmbeddedModels_PrunesRestrictedPanels_AndEnforcesVerticalIsolation()
  {
    var catalog = new ModuleCatalog();
    catalog.Register(new TestAppModuleImpl());

    var screenSvc = new TestCompositeScreenService();
    var screen = await screenSvc.BuildScreenAsync("test/composite-screen", CancellationToken.None);

    Assert.NotNull(screen.Layout?.Root.Children);
    Assert.Equal(2, screen.Layout!.Root.Children!.Count);

    var leftModel = Assert.IsType<PanelScreenModel>(screen.Layout.Root.Children[0].Model);
    var rightModel = Assert.IsType<PanelScreenModel>(screen.Layout.Root.Children[1].Model);
    Assert.Equal("test/counted-invoice-grid", leftModel.Service);
    Assert.Equal("test/summary-report", rightModel.Service);

    var restrictedUser = new FixedUser(10, TestAppModule.CountedInvoices.View);
    ScreenService.EnforceAndFilterScreen(typeof(TestCompositeScreenService), screen, restrictedUser, catalog);
    var surviving = Assert.Single(screen.Layout.Root.Children);
    Assert.Equal("test/counted-invoice-grid", Assert.IsType<PanelScreenModel>(surviving.Model).Service);

    var badCatalog = new ModuleCatalog();
    badCatalog.Register(new TestAppModuleImpl());
    badCatalog.Register(new MockParallelScreenModuleImpl());
    var ex = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(badCatalog));
    Assert.Contains("Module isolation violation", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task DemoGoldenReference_ValidatesCleanly_AndRendersWorkspaceAndReportInEnglishAndUkrainian()
  {
    var demoCatalog = new ModuleCatalog();
    demoCatalog.Register(new AppModule());
    demoCatalog.Register(new OrgModule());
    demoCatalog.Register(new EcomModule());

    PlatformValidator.Validate(demoCatalog);
    Localizer.Instance.Register(typeof(EcomResource));

    var prevUi = CultureInfo.CurrentUICulture;
    try
    {
      CultureInfo.CurrentUICulture = new CultureInfo("uk-UA");
      var workspace = new EcomSalesWorkspaceScreenService();
      var wsDto = await workspace.BuildScreenAsync("ecom/sales-workspace", CancellationToken.None);
      Assert.NotNull(wsDto.Layout?.Root.Children);
      Assert.Equal(2, wsDto.Layout!.Root.Children!.Count);
      Assert.Equal("Замовлення клієнтів", wsDto.Layout.Root.Children[0].Label);
      Assert.Equal("ecom/customer-order-grid", Assert.IsType<PanelScreenModel>(wsDto.Layout.Root.Children[0].Model).Service);
      Assert.Equal("ecom/stock-balance-grid", Assert.IsType<PanelScreenModel>(wsDto.Layout.Root.Children[1].Model).Service);

      await using var ecomConn = new SqliteConnection("DataSource=:memory:");
      await ecomConn.OpenAsync();
      var tenant = new FixedTenant(1, 10, new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase));
      await using (var appDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ecomConn).Options, tenant))
      {
        await appDb.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
      }

      await using (var orgDb = new OrgDbContext(new DbContextOptionsBuilder<OrgDbContext>().UseSqlite(ecomConn).Options, tenant))
      {
        await orgDb.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
      }

      var ecomOptions = new DbContextOptionsBuilder<EcomDbContext>().UseSqlite(ecomConn).Options;
      await using var ecomDb = new EcomDbContext(ecomOptions, tenant);
      await ecomDb.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

      var report = new EcomSalesReportService(ecomDb);
      var reportDto = await report.GetReportAsync(new ReportRequest(), CancellationToken.None);
      Assert.Equal(4, reportDto.Kpis!.Count);
      Assert.Equal("Виручка", reportDto.Kpis[1].Label);
    }
    finally
    {
      CultureInfo.CurrentUICulture = prevUi;
    }
  }
}
