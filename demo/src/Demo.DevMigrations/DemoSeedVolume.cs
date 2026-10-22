namespace Demo.DevMigrations;

public sealed record DemoSeedVolume(OrgSeedVolume Org, EcomSeedVolume Ecom)
{
  public static DemoSeedVolume Parse(string value) =>
    new(OrgSeedVolume.Parse(value), EcomSeedVolume.Parse(value));
}
