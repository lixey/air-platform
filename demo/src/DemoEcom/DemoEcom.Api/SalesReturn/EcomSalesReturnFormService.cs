namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.SalesReturnId;

  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Span(3);
    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(6);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService>().Required().Span(6);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomSalesReturnLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomSalesReturnFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomSalesReturnBasisGridService>().Key("basis").Label("Based on");
    elements.AddChild<EcomSalesReturnHistoryGridService>().Key("history").Label("History");
  }
}
