namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferFormService(EcomDbContext db, EcomStockTransferFlowService flow, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomDocTypeData.StockTransferId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetElements()
  {
    Elements.UseRecordLabelOf<EcomStockTransferGridService>();
    Elements.Add(e => e.Number).AsInfo().Span(3);
    Elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    Elements.Disable(Frozen);
    Elements.Add(e => e.Date).Span(3);
    Elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    Elements.Add(e => e.Warehouse).Label("From warehouse").AsReference<OrgWarehouseGridService>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(6);
    Elements.Add(e => e.WarehouseTo).Label("To warehouse").AsReference<OrgWarehouseGridService>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(6);
    Elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    Elements.Add(e => e.Note).AsTextarea();

    Elements.AddChild<EcomStockTransferLineGridService, EcomDocLine>()
      .Key("lines")
      .Label("Lines")
      .SumTo(d => d.Qty, l => l.Qty)
      .SumTo(d => d.Amount, l => l.Amount);
    Elements.AddChild<EcomStockTransferHistoryGridService>().Key("history").Label("History");
  }

  protected override void SetRules()
  {
    var texts = EcomDocTexts.Of(L);
    Rules.For(e => e.WarehouseToId)
      .Must((doc, warehouse) => EcomDocRules.WarehousesDiffer(doc.WarehouseId, warehouse))
      .WithMessage(texts.SameWarehouse);
  }
}
