namespace DemoEcom.CustomerOrderFulfillment;

public sealed class EcomCustomerOrderFulfillmentRow
{
  public long Id { get; set; }

  public int Ver { get; set; }

  public DocPart? Doc { get; set; }

  public ProductPart? Product { get; set; }

  public decimal Qty { get; set; }

  public decimal QtyReserved { get; set; }

  public decimal QtyPurchased { get; set; }

  public decimal QtyDone { get; set; }

  public decimal Short { get; set; }

  public decimal ToPurchase { get; set; }

  public sealed class DocPart
  {
    public string? Number { get; set; }

    public DateOnly Date { get; set; }

    public Reference? DocStatus { get; set; }

    public Reference? Organization { get; set; }

    public Reference? Warehouse { get; set; }

    public Reference? Customer { get; set; }
  }

  public sealed class ProductPart
  {
    public string? Article { get; set; }

    public string? Name { get; set; }

    public Reference? Supplier { get; set; }
  }
}
