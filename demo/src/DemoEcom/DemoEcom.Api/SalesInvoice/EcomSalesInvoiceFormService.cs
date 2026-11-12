namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceFormService(EcomDbContext db, EcomSalesInvoiceFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomDocTypeData.SalesInvoiceId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomSalesInvoiceGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(3);
    elements.Add(e => e.Contact).AsReference<OrgCustomerContactGridService>()
      .Where(c => c.CustomerId, e => e.CustomerId).Span(3);
    elements.Add(e => e.Warehouse).AsReference<OrgWarehouseGridService>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(3);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(3);
    elements.AddSeparator("Delivery and payment");
    elements.Add(e => e.DeliveryTypeId).AsReference<OrgDeliveryTypeGridService>().Span(4);
    elements.Add(e => e.PaymentMethodId).AsReference<OrgPaymentMethodGridService>().Span(4);
    elements.Add(e => e.PaymentTermId).AsReference<OrgPaymentTermGridService>().Span(4);
    elements.Add(e => e.RecipientName).Span(6);
    elements.Add(e => e.RecipientPhone).AsPhone("+380 (##) ###-##-##").Span(6);
    elements.Add(e => e.DeliveryAddress).Span(8);
    elements.Add(e => e.TrackingNumber).Disabled(false).Span(4);
    elements.Add(e => e.CounterpartyId).AsReference<OrgCounterpartyGridService>().Span(12);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomSalesInvoiceLineGridService, EcomDocLine>()
      .Key("lines")
      .Label("Lines")
      .SumTo(d => d.Qty, l => l.Qty)
      .SumTo(d => d.Amount, l => l.Amount);
    elements.AddChild<EcomSalesInvoiceBasisGridService>().Key("basis").Label("Entered on");
    elements.AddChild<EcomSalesInvoiceFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomSalesInvoiceHistoryGridService>().Key("history").Label("History");
  }
}
