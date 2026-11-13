using Microsoft.AspNetCore.Http;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class AppRbacAndValidatorTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public AppRbacAndValidatorTests()
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
  public void RightsTree_ProjectsFrozenFeatures_IncludesFlowTransitions_AndOmitsDisabledExport()
  {
    var catalog = new ModuleCatalog();
    catalog.Register(new TestAppModuleImpl());

    Assert.True(TestAppModule.Invoices.IsFrozen);
    Assert.True(TestAppModule.SummaryReport.IsFrozen);
    Assert.False(TestAppModule.SummaryReport.HasExport);

    var held = new HashSet<string>(["Test.Invoices.Approve"], StringComparer.OrdinalIgnoreCase);
    var tree = RightsTree.Build(catalog, held);
    var rootNode = Assert.Single(tree);
    Assert.Equal("*", rootNode.Key);

    var modNode = Assert.Single(rootNode.Children!);
    Assert.Equal("Test.*", modNode.Key);

    var invoicesNode = Assert.Single(modNode.Children!, n => n.Key == "Test.Invoices.*");
    var approveNode = Assert.Single(invoicesNode.Children!, n => n.Key == "Test.Invoices.Approve");
    Assert.False(approveNode.IsDisabled);
    var editNode = Assert.Single(invoicesNode.Children!, n => n.Key == "Test.Invoices.Edit");
    Assert.True(editNode.IsDisabled);
    Assert.Contains(invoicesNode.Children!, n => n.Key == "Test.Invoices.Export");

    var reportNode = Assert.Single(modNode.Children!, n => n.Key == "Test.SummaryReport.*");
    Assert.DoesNotContain(reportNode.Children!, n => n.Key == "Test.SummaryReport.Edit");
    Assert.DoesNotContain(reportNode.Children!, n => n.Key == "Test.SummaryReport.Delete");
    Assert.DoesNotContain(reportNode.Children!, n => n.Key == "Test.SummaryReport.Export");

    var flattened = RightsTree.FlattenKeys(tree);
    Assert.Contains("Test.Invoices.Approve", flattened);
    Assert.DoesNotContain("Test.SummaryReport.Export", flattened);
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
  public async Task GridAndForm_FailClosedReadOnly_WhenMissingPlatformAppAndUnauthenticated()
  {
    await using var db = CreateDbContext();
    var inv = new TestInvoice { Number = "INV-RO", Status = 1 };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();

    var unauthGrid = new TestInvoiceGridService(db, app: null);
    var gridScreen = await unauthGrid.BuildScreenAsync("test/invoice", new GridRequest(), CancellationToken.None);
    Assert.True(FindPanelModel<GridModel>(gridScreen.Layout!.Root).IsReadOnly);

    var unauthForm = new TestInvoiceFormService(db, app: null);
    var formScreen = await unauthForm.BuildScreenAsync("test/invoice-form", inv.Id, CancellationToken.None);
    Assert.True(FindPanelModel<FormModel>(formScreen.Layout!.Root).IsReadOnly);
  }

  [Fact]
  public async Task DispatchEndpointAsync_BindsPlatformAppToReportAndScreenServices()
  {
    await using var db = CreateDbContext();
    var user = new FixedUser(42, "*");
    var app = new TestPlatformAppService(db, user);
    var report = new TestSummaryReportService();
    var screen = new TestOverviewScreenService();

    var services = new ServiceCollection()
      .AddLogging()
      .AddSingleton<IUserContext>(user)
      .AddSingleton<PlatformAppService>(app)
      .AddSingleton(report)
      .AddSingleton(screen)
      .BuildServiceProvider();

    var reportCtx = new DefaultHttpContext { RequestServices = services };
    reportCtx.Request.Path = "/api/test/summary-report/get-data";
    var reportRes = await ServiceEndpointsExtensions.DispatchEndpointAsync(
      typeof(TestSummaryReportService),
      report,
      "get-data",
      TestAppModule.SummaryReport,
      reportCtx.Request,
      services,
      CancellationToken.None);
    await reportRes.ExecuteAsync(reportCtx);

    Assert.Equal(StatusCodes.Status200OK, reportCtx.Response.StatusCode);
    Assert.Same(app, report.CurrentAppService);
    Assert.Equal(42L, report.LastSeenUserId);

    var screenCtx = new DefaultHttpContext { RequestServices = services };
    screenCtx.Request.Path = "/api/test/overview-screen/get-data";
    var screenRes = await ServiceEndpointsExtensions.DispatchEndpointAsync(
      typeof(TestOverviewScreenService),
      screen,
      "get-data",
      TestAppModule.OverviewScreen,
      screenCtx.Request,
      services,
      CancellationToken.None);
    await screenRes.ExecuteAsync(screenCtx);

    Assert.Equal(StatusCodes.Status200OK, screenCtx.Response.StatusCode);
    Assert.Same(app, screen.CurrentAppService);
    Assert.Equal(42L, screen.LastSeenUserId);
  }

  [Fact]
  public void PlatformValidator_ValidatesCleanModule_AndCatchesBrokenActionsFlowsAndHorizontalCoupling()
  {
    var validCatalog = new ModuleCatalog();
    validCatalog.Register(new TestAppModuleImpl());
    PlatformValidator.Validate(validCatalog);

    // Secondary services (Form, ChildGrid, Flow) resolve to their own service types, never the root GridService
    Assert.Equal(typeof(TestInvoiceGridService), validCatalog.FindServiceType("test/invoice-grid") ?? validCatalog.FindFeature("test/invoice-grid")?.ServiceType);
    Assert.Equal(typeof(TestInvoiceGridService), validCatalog.FindServiceType("test/invoices") ?? validCatalog.FindFeature("test/invoices")?.ServiceType);
    Assert.Equal(typeof(TestInvoiceFormService), validCatalog.FindServiceType("test/invoice-form") ?? validCatalog.FindFeature("test/invoice-form")?.ServiceType);
    Assert.Equal(typeof(TestInvoiceLineGridService), validCatalog.FindServiceType("test/invoice-line-grid") ?? validCatalog.FindFeature("test/invoice-line-grid")?.ServiceType);
    Assert.Same(TestAppModule.Invoices, validCatalog.FindFeature("test/invoice-form"));

    // 1. Unattributed Nav.AddAction target method
    var badActionCatalog = new ModuleCatalog();
    badActionCatalog.Register(new MockBadActionModuleImpl());
    var badActionEx = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(badActionCatalog));
    Assert.Contains("UnattributedRun", badActionEx.Message, StringComparison.Ordinal);

    // 2. StatusFlowService transition pointing to undeclared target status ID
    var badFlowCatalog = new ModuleCatalog();
    badFlowCatalog.Register(new MockBadFlowModuleImpl());
    var badFlowEx = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(badFlowCatalog));
    Assert.Contains("999", badFlowEx.Message, StringComparison.Ordinal);

    // 3. Horizontal cross-module service reference (ADR #8)
    var badHorizCatalog = new ModuleCatalog();
    badHorizCatalog.Register(new MockHorizModuleImpl());
    var badHorizEx = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(badHorizCatalog));
    Assert.Contains("Module isolation violation", badHorizEx.Message, StringComparison.Ordinal);
  }
}
