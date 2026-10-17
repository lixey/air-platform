using DemoEcom.Data;
using DemoEcom.Documents;
using DemoOrg.Data.Models;

namespace DemoEcom.StockIssue;

public sealed class EcomStockIssueFormService(EcomDbContext db, EcomStockIssueFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.StockIssueId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockIssue;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomStockIssueGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService, OrgWarehouse>().Required().Span(6);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomStockIssueLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomStockIssueHistoryGridService>().Key("history").Label("History");
  }
}
