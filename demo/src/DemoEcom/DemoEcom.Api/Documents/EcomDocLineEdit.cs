namespace DemoEcom.Documents;

public sealed class EcomDocLineEdit
{
  public long ProductId { get; set; }

  public decimal Qty { get; set; }

  public decimal? Price { get; set; }

  public decimal? DiscountPercent { get; set; }

  public decimal Amount { get; set; }

  public string? Note { get; set; }
}
