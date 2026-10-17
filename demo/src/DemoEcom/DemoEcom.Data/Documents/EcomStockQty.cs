namespace DemoEcom.Data.Documents;

/// <summary>
/// The three quantities of the stock: what is there, what of it is reserved and what is expected. In a move or a
/// difference each is signed.
/// </summary>
public readonly record struct EcomStockQty(decimal Qty, decimal Reserved, decimal Expected)
{
  /// <summary>Whether all three are zero.</summary>
  public bool IsZero => Qty == 0m && Reserved == 0m && Expected == 0m;

  /// <summary>The free stock: the quantity less the reserved.</summary>
  public decimal Free => Qty - Reserved;

  /// <summary>These quantities with <paramref name="other"/> added, each to its own.</summary>
  public EcomStockQty Plus(EcomStockQty other) => new(Qty + other.Qty, Reserved + other.Reserved, Expected + other.Expected);

  /// <summary>These quantities with <paramref name="other"/> taken away, each from its own.</summary>
  public EcomStockQty Minus(EcomStockQty other) => new(Qty - other.Qty, Reserved - other.Reserved, Expected - other.Expected);
}
