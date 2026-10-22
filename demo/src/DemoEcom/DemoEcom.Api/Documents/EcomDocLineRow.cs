namespace DemoEcom.Documents;

public sealed class EcomDocLineRow
{
  public long Id { get; set; }
  public int Ver { get; set; }
  public int SortOrder { get; set; }
  public Reference? Product { get; set; }
  public string? Article { get; set; }
  public decimal Qty { get; set; }
  public decimal QtyReserved { get; set; }
  public decimal QtyDone { get; set; }
  public decimal Price { get; set; }
  public decimal DiscountPercent { get; set; }
  public decimal Amount { get; set; }
}
