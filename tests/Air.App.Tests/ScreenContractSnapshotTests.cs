using System.Globalization;
using System.Text.Encodings.Web;
using DemoApp;
using DemoApp.Data;
using DemoApp.Data.Models;
using DemoEcom;
using DemoEcom.CustomerOrder;
using DemoEcom.Data;
using DemoEcom.Data.Models;
using DemoEcom.Resources;
using DemoEcom.SalesWorkspace;
using DemoOrg;
using DemoOrg.Data;
using DemoOrg.Resources;
using DemoOrg.Warehouse;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class ScreenContractSnapshotTests
{
  private static readonly DateTimeOffset FixedUtcNow = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

  [Fact]
  public async Task DemoRepresentativeScreens_MatchGoldenWireContractSnapshots()
  {
    ModuleCatalog.Instance.Register(new AppModule());
    ModuleCatalog.Instance.Register(new OrgModule());
    ModuleCatalog.Instance.Register(new EcomModule());
    Localizer.Instance.Register(typeof(OrgResource));
    Localizer.Instance.Register(typeof(EcomResource));

    var prevCulture = CultureInfo.CurrentCulture;
    var prevUiCulture = CultureInfo.CurrentUICulture;
    try
    {
      var en = new CultureInfo("en-US");
      CultureInfo.CurrentCulture = en;
      CultureInfo.CurrentUICulture = en;

      await using var connection = new SqliteConnection("DataSource=:memory:");
      await connection.OpenAsync();

      var tenant = new FixedTenant(1, 10, new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase));
      var user = new FixedUser(10, "*");
      await using var services = new ServiceCollection()
        .AddSingleton<TimeProvider>(new FixedTimeProvider(FixedUtcNow))
        .BuildServiceProvider();

      await using var appDb = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
        tenant);
      await appDb.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
      appDb.AppColor.Add(new AppColor { Id = 1, Handle = "Sky", Name = "Sky", SortOrder = 1 });
      await appDb.SaveChangesAsync();

      await using var orgDb = new OrgDbContext(
        new DbContextOptionsBuilder<OrgDbContext>().UseSqlite(connection).Options,
        tenant);
      await orgDb.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

      await using var ecomDb = new EcomDbContext(
        new DbContextOptionsBuilder<EcomDbContext>().UseSqlite(connection).Options,
        tenant);
      await ecomDb.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
      SeedCustomerOrderMetadata(ecomDb);
      await ecomDb.SaveChangesAsync();

      var orgApp = new OrgService(user, services, tenant, Localizer.Instance);
      var ecomApp = new EcomService(user, services, tenant, Localizer.Instance);
      var orderFlow = new EcomCustomerOrderFlowService(ecomDb);

      var workspaceScreen = await new EcomSalesWorkspaceScreenService()
        .BuildScreenAsync("ecom/sales-workspace", CancellationToken.None);
      AssertMatchesSnapshot("ecom-sales-workspace.json", workspaceScreen);

      var orderGridScreen = await new EcomCustomerOrderGridService(ecomDb, ecomApp, orderFlow)
        .BuildScreenAsync("ecom/customer-order-grid", new GridRequest(), CancellationToken.None);
      AssertMatchesSnapshot("ecom-customer-order-grid.json", orderGridScreen);

      var orderForm = new EcomCustomerOrderFormService(ecomDb, orderFlow, ecomApp);
      Assert.Equal(OverlayPresentation.Drawer, (await orderForm.OverlayAsync("ecom/customer-order-form", CancellationToken.None)).Presentation);
      var orderFormScreen = await orderForm.BuildScreenAsync("ecom/customer-order-form", new FormRequest(), CancellationToken.None);
      AssertMatchesSnapshot("ecom-customer-order-form.json", orderFormScreen);

      var warehouseGridScreen = await new OrgWarehouseGridService(orgDb, orgApp)
        .BuildScreenAsync("org/warehouse-grid", new GridRequest(), CancellationToken.None);
      AssertMatchesSnapshot("org-warehouse-grid.json", warehouseGridScreen);

      var warehouseForm = new OrgWarehouseFormService(orgDb, orgApp);
      Assert.Equal(OverlayPresentation.Popover, (await warehouseForm.OverlayAsync("org/warehouse-form", CancellationToken.None)).Presentation);
      var warehouseFormScreen = await warehouseForm.BuildScreenAsync("org/warehouse-form", new FormRequest(), CancellationToken.None);
      AssertMatchesSnapshot("org-warehouse-form.json", warehouseFormScreen);
    }
    finally
    {
      CultureInfo.CurrentCulture = prevCulture;
      CultureInfo.CurrentUICulture = prevUiCulture;
    }
  }

  private static void SeedCustomerOrderMetadata(EcomDbContext db)
  {
    db.EcomDocType.Add(new EcomDocType
    {
      Id = EcomDocTypeData.CustomerOrderId,
      Handle = "CustomerOrder",
      Name = "Customer order",
      Prefix = "CO",
      SortOrder = 1,
    });
    db.EcomDocStatus.AddRange(
      new EcomDocStatus
      {
        Id = EcomDocStatusData.CustomerOrderNewId,
        DocTypeId = EcomDocTypeData.CustomerOrderId,
        ColorId = 1,
        Handle = EcomDocStatusData.CustomerOrderNew,
        Name = "New",
        IsEditable = true,
        SortOrder = 1,
      },
      new EcomDocStatus
      {
        Id = EcomDocStatusData.CustomerOrderInProgressId,
        DocTypeId = EcomDocTypeData.CustomerOrderId,
        ColorId = 1,
        Handle = EcomDocStatusData.CustomerOrderInProgress,
        Name = "In progress",
        IsEditable = true,
        SortOrder = 2,
      },
      new EcomDocStatus
      {
        Id = EcomDocStatusData.CustomerOrderConfirmedId,
        DocTypeId = EcomDocTypeData.CustomerOrderId,
        ColorId = 1,
        Handle = EcomDocStatusData.CustomerOrderConfirmed,
        Name = "Confirmed",
        IsEditable = true,
        IsPosted = true,
        SortOrder = 3,
      },
      new EcomDocStatus
      {
        Id = EcomDocStatusData.CustomerOrderPausedId,
        DocTypeId = EcomDocTypeData.CustomerOrderId,
        ColorId = 1,
        Handle = EcomDocStatusData.CustomerOrderPaused,
        Name = "Paused",
        IsPosted = true,
        SortOrder = 4,
      },
      new EcomDocStatus
      {
        Id = EcomDocStatusData.CustomerOrderCompletedId,
        DocTypeId = EcomDocTypeData.CustomerOrderId,
        ColorId = 1,
        Handle = EcomDocStatusData.CustomerOrderCompleted,
        Name = "Completed",
        IsPosted = true,
        IsFinal = true,
        SortOrder = 5,
      },
      new EcomDocStatus
      {
        Id = EcomDocStatusData.CustomerOrderCanceledId,
        DocTypeId = EcomDocTypeData.CustomerOrderId,
        ColorId = 1,
        Handle = EcomDocStatusData.CustomerOrderCanceled,
        Name = "Canceled",
        IsFinal = true,
        IsCanceled = true,
        SortOrder = 6,
      });
  }

  private static void AssertMatchesSnapshot(string snapshotFileName, ScreenDto screen)
  {
    var options = new JsonSerializerOptions
    {
      WriteIndented = true,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    PlatformJsonOptions.ConfigurePlatformJson(options);

    var actual = NormalizeLineEndings(JsonSerializer.Serialize(screen, options)) + "\n";
    var snapshotPath = ResolveSnapshotPath(snapshotFileName);

    if (string.Equals(Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS"), "1", StringComparison.Ordinal))
    {
      Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
      File.WriteAllText(snapshotPath, actual);
      return;
    }

    Assert.True(
      File.Exists(snapshotPath),
      $"Missing golden screen contract snapshot '{snapshotFileName}'. Re-run with UPDATE_SNAPSHOTS=1 if intentional.");

    var expected = NormalizeLineEndings(File.ReadAllText(snapshotPath)).TrimEnd('\n') + "\n";
    Assert.Equal(expected, actual);
  }

  private static string NormalizeLineEndings(string text) =>
    text.Replace("\r\n", "\n", StringComparison.Ordinal);

  private static string ResolveSnapshotPath(string fileName)
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AirPlatform.slnx")))
    {
      dir = dir.Parent;
    }

    var root = dir?.FullName
      ?? throw new InvalidOperationException("Unable to locate repository root containing AirPlatform.slnx.");
    return Path.Combine(root, "tests", "Air.App.Tests", "Snapshots", fileName);
  }
}
