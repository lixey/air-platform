namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderProductRow
{
  public long Id { get; set; }
  public string Article { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public Reference? Brand { get; set; }
  public decimal Price { get; set; }
  public bool InOrder { get; set; }
  public decimal? Free { get; set; }
}
