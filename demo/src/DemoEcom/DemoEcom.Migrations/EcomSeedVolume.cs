namespace DemoEcom.Migrations;

public sealed record EcomSeedVolume(
  int CustomerOrders,
  int PurchaseOrders,
  int SupplierInvoices,
  int StockIssues,
  int StockTransfers,
  int OpeningLines
)
{
  public static EcomSeedVolume Parse(string value) => value switch
  {
    "small" => new(100, 20, 20, 6, 6, 30),
    "default" => new(1000, 100, 100, 20, 20, 100),
    "medium" => new(10000, 1000, 1000, 100, 100, 300),
    "large" => new(100000, 5000, 5000, 500, 500, 500),
    _ => throw new InvalidOperationException("The volume must be small, default, medium or large.")
  };
}
