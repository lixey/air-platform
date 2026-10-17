namespace DemoEcom.CustomerOrderFulfillment;

public sealed class EcomCustomerOrderFulfillmentRow
{
  public long Id { get; set; }
  public string DocNumber { get; set; } = string.Empty;
  public DateOnly DocDate { get; set; }
  public string ProductName { get; set; } = string.Empty;
  public decimal Qty { get; set; }
  public decimal QtyReserved { get; set; }
  public decimal QtyPurchased { get; set; }
  public decimal QtyDone { get; set; }
  public decimal ToPurchase { get; set; }
}
