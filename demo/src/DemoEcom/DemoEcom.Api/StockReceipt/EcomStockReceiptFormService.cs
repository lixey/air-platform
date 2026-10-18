using DemoEcom.Data;
using DemoEcom.Documents;
using DemoOrg.Data.Models;

namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptFormService(EcomDbContext db, EcomStockReceiptFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.StockReceiptId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockReceipt;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomStockReceiptGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService, OrgWarehouse>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(6);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomStockReceiptLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomStockReceiptHistoryGridService>().Key("history").Label("History");
  }
}
