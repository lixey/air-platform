namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.StockTransferId;

  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Span(3);
    elements.Add(e => e.WarehouseId).Label("Source Warehouse").AsReference<OrgWarehouseGridService>().Required().Span(6);
    elements.Add(e => e.WarehouseToId).Label("Destination Warehouse").AsReference<OrgWarehouseGridService>().Required().Span(6);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomStockTransferLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomStockTransferFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomStockTransferHistoryGridService>().Key("history").Label("History");
  }
}
