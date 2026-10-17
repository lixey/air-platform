namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.SalesInvoiceId;

  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Span(3);
    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(6);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService>().Required().Span(6);
    elements.Add(e => e.DeliveryAddress).Span(12);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomSalesInvoiceLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomSalesInvoiceFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomSalesInvoiceBasisGridService>().Key("basis").Label("Based on");
    elements.AddChild<EcomSalesInvoiceHistoryGridService>().Key("history").Label("History");
  }
}
