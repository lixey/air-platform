using DemoApp.Data;
using DemoApp.Migrations.Seeds;

namespace DemoApp.Migrations;

public static class AppSeed
{
  public static async Task RunAsync(
    AppDbContext db,
    int userGroupsCount = 20,
    int usersCount = 50,
    int randomSeed = 1729,
    IReadOnlyList<AppSeedTenant>? tenants = null,
    CancellationToken cancellationToken = default
  )
  {
    tenants ??= AppCodeRows.Tenants();

    await AppCodeRows.SyncAsync(db, tenants, cancellationToken);
    await AppIdentitySeed.RunAsync(db, tenants, cancellationToken);
    await AppVolumeSeed.RunAsync(db, userGroupsCount, usersCount, randomSeed, tenants, cancellationToken);
  }
}
