using DemoApp.Migrations.Seeds;
using DemoEcom.Data;
using DemoEcom.Migrations.Seeds;

namespace DemoEcom.Migrations;

public static class EcomSeed
{
  public static async Task RunAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int randomSeed = 1729,
    IReadOnlyList<AppSeedTenant>? tenants = null,
    CancellationToken cancellationToken = default
  )
  {
    tenants ??= AppCodeRows.Tenants();

    await EcomCodeRows.SyncAsync(db, cancellationToken);
    await EcomStockSeed.RunAsync(db, tenants, cancellationToken);
    await EcomSalesSeed.RunAsync(db, volume, randomSeed, tenants, cancellationToken);
  }
}
