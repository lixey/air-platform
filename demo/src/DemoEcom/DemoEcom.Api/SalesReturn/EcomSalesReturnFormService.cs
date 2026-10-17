using DemoEcom.Data;
using DemoEcom.Documents;
using DemoOrg.Data.Models;

namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnFormService(EcomDbContext db, EcomSalesReturnFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.SalesReturnId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomSalesReturnGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(4);
    elements.Add(e => e.ContactId).AsReference<OrgCustomerContactGridService, OrgCustomerContact>().Span(4);
    elements.Add(e => e.CounterpartyId).AsReference<OrgCounterpartyGridService>().Span(4);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService, OrgWarehouse>().Required().Span(6);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomSalesReturnLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomSalesReturnBasisGridService>().Key("basis").Label("Entered on");
    elements.AddChild<EcomSalesReturnFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomSalesReturnHistoryGridService>().Key("history").Label("History");
  }
}
