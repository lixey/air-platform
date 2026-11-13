namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnFormService(EcomDbContext db, EcomSalesReturnFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomDocTypeData.SalesReturnId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetElements()
  {
    Elements.UseRecordLabelOf<EcomSalesReturnGridService>();
    Elements.Add(e => e.Number).AsInfo().Span(3);
    Elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    Elements.Disable(Frozen);
    Elements.Add(e => e.Date).Span(3);
    Elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    Elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(4);
    Elements.Add(e => e.Contact).AsReference<OrgCustomerContactGridService>()
      .Where(c => c.CustomerId, e => e.CustomerId).Span(4);
    Elements.Add(e => e.CounterpartyId).AsReference<OrgCounterpartyGridService>().Span(4);
    Elements.Add(e => e.Warehouse).AsReference<OrgWarehouseGridService>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(6);
    Elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    Elements.Add(e => e.Note).AsTextarea();

    Elements.AddChild<EcomSalesReturnLineGridService, EcomDocLine>()
      .Key("lines")
      .Label("Lines")
      .SumTo(d => d.Qty, l => l.Qty)
      .SumTo(d => d.Amount, l => l.Amount);
    Elements.AddChild<EcomSalesReturnBasisGridService>().Key("basis").Label("Entered on");
    Elements.AddChild<EcomSalesReturnFollowerGridService>().Key("followers").Label("Followed by");
    Elements.AddChild<EcomSalesReturnHistoryGridService>().Key("history").Label("History");
  }
}
