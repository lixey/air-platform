namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.SupplierInvoiceId;

  public override Feature Feature => Ecom.SupplierInvoice;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Span(3);
    elements.Add(e => e.CounterpartyId).Label("Supplier").AsReference<OrgSupplierGridService>().Required().Span(6);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService>().Required().Span(6);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomSupplierInvoiceLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomSupplierInvoiceFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomSupplierInvoiceBasisGridService>().Key("basis").Label("Based on");
    elements.AddChild<EcomSupplierInvoiceHistoryGridService>().Key("history").Label("History");
  }
}
