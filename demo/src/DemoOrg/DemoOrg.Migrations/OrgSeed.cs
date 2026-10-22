namespace DemoOrg.Migrations;

public static class OrgSeed
{
  public static async Task RunAsync(
    OrgDbContext db,
    OrgSeedVolume volume,
    int randomSeed = 1729,
    IReadOnlyList<AppSeedTenant>? tenants = null,
    CancellationToken cancellationToken = default
  )
  {
    tenants ??= AppCodeRows.Tenants();

    await OrgCodeRows.SyncAsync(db, cancellationToken);
    await OrgExampleSeed.RunAsync(db, volume, tenants, cancellationToken);
    await OrgPartySeed.RunAsync(db, volume, randomSeed, tenants, cancellationToken);
    await OrgCatalogueSeed.RunAsync(db, volume, randomSeed, tenants, cancellationToken);
  }
}
