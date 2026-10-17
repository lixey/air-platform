namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.ProductId).AsReference<OrgProductGridService>().Required().Span(12);
    elements.Add(e => e.Qty).Label("Quantity").Span(3);
    elements.Add(e => e.Price).AsMoney().Span(3);
    elements.Add(e => e.DiscountPercent).Label("Discount %").AsPercent().Range(0, 100).Span(3);
    elements.Add(e => e.Amount).AsMoney().Span(3);
    elements.Add(e => e.Note).AsTextarea().Span(12);
  }
}
