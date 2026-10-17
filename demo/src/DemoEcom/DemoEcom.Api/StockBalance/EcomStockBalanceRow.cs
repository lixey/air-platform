namespace DemoEcom.StockBalance;

public sealed class EcomStockBalanceRow
{
  public long Id { get; set; }

  public Reference? Organization { get; set; }

  public Reference? Warehouse { get; set; }

  public Reference? Product { get; set; }

  public string? Article { get; set; }

  public decimal Qty { get; set; }

  public decimal Reserved { get; set; }

  public decimal Expected { get; set; }

  public decimal Free { get; set; }
}
