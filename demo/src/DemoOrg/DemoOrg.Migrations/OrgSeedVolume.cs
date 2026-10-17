namespace DemoOrg.Migrations;

public sealed record OrgSeedVolume(
  int Organizations,
  int Warehouses,
  int MoneyAccounts,
  int Customers,
  int Suppliers,
  int Buyers,
  int Brands,
  int Categories,
  int Products
)
{
  public static OrgSeedVolume Parse(string value) => value switch
  {
    "small" => new(2, 4, 4, 50, 10, 5, 20, 10, 200),
    "default" => new(3, 7, 8, 500, 20, 50, 60, 20, 2000),
    "medium" => new(3, 7, 8, 5000, 40, 200, 60, 20, 20000),
    "large" => new(3, 7, 8, 50000, 100, 2000, 60, 20, 200000),
    _ => throw new InvalidOperationException("The volume must be small, default, medium or large.")
  };
}
