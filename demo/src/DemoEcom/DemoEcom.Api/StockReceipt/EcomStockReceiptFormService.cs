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
    elements.Add(e => e.Warehouse).AsReference<OrgWarehouseGridService>()
      .Where(w => w.OrganizationId, e => e.OrganizationId).Required().Span(6);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomStockReceiptLineGridService, EcomDocLine>()
      .Key("lines")
      .Label("Lines")
      .SumTo(d => d.Qty, l => l.Qty)
      .SumTo(d => d.Amount, l => l.Amount);
    elements.AddChild<EcomStockReceiptHistoryGridService>().Key("history").Label("History");
  }
}
