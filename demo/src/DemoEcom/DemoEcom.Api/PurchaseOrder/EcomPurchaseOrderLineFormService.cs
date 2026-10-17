namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomPurchaseOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.ProductId).AsReference<OrgProductGridService>().Required().Span(12);
    elements.Add(e => e.Qty).Label("Quantity").Span(4);
    elements.Add(e => e.Price).AsMoney().Span(4);
    elements.Add(e => e.Amount).AsMoney().Span(4);
    elements.Add(e => e.Note).AsTextarea().Span(12);
  }
}
