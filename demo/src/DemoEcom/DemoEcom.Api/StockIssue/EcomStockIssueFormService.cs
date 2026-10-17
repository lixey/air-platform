namespace DemoEcom.StockIssue;

public sealed class EcomStockIssueFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.StockIssueId;

  public override Feature Feature => Ecom.StockIssue;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Span(3);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService>().Required().Span(12);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomStockIssueLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomStockIssueFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomStockIssueHistoryGridService>().Key("history").Label("History");
  }
}
