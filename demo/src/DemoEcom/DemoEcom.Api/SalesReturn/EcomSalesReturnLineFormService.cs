namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomSalesReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesReturn;

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
