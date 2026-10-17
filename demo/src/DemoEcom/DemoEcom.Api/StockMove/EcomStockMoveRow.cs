namespace DemoEcom.StockMove;

public sealed class EcomStockMoveRow
{
  public long Id { get; set; }

  public DateOnly Date { get; set; }

  public string? Number { get; set; }

  public string? Type { get; set; }

  public Reference? Organization { get; set; }

  public Reference? Warehouse { get; set; }

  public Reference? Product { get; set; }

  public string? Article { get; set; }

  public decimal Qty { get; set; }

  public decimal Reserved { get; set; }

  public decimal Expected { get; set; }
}
