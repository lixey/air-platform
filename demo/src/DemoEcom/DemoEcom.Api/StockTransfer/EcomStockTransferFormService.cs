using DemoEcom.Data;
using DemoEcom.Documents;
using DemoOrg.Data.Models;

namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferFormService(EcomDbContext db, EcomStockTransferFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.StockTransferId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomStockTransferGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.WarehouseId).Label("From warehouse").AsReference<OrgWarehouseGridService, OrgWarehouse>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(6);
    elements.Add(e => e.WarehouseToId).Label("To warehouse").AsReference<OrgWarehouseGridService, OrgWarehouse>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(6);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomStockTransferLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomStockTransferHistoryGridService>().Key("history").Label("History");
  }

  protected override void SetRules(Rules rules)
  {
    var texts = EcomDocTexts.Of(L);
    rules.For(e => e.WarehouseToId)
      .Must((doc, warehouse) => EcomDocRules.WarehousesDiffer(doc.WarehouseId, warehouse))
      .WithMessage(texts.SameWarehouse);
  }
}
