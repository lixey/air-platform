namespace DemoEcom.StockIssue;

/// <summary>
/// The status flow of a stock issue.
/// </summary>
public sealed class EcomStockIssueFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.StockIssueDraftId;
  private const long Posted = EcomData.DocStatus.StockIssuePostedId;
  private const long Canceled = EcomData.DocStatus.StockIssueCanceledId;

  protected override long Type => EcomData.DocType.StockIssueId;

  public override Feature Feature => Ecom.StockIssue;

  protected override void SetFlow(Flow flow)
  {
    base.SetFlow(flow);
    flow.Status(Draft, "Draft", UiColor.Slate);
    flow.Status(Posted, "Posted", UiColor.Green);
    flow.FinalStatus(Canceled, "Canceled", UiColor.Red);
    flow.Initial(Draft);
  }

  public Transition Reopen => FromOpen().To(Draft).Do(PostAsync);

  public Transition Post => FromOpen().To(Posted).Do(PostAsync);

  public Transition Cancel => FromOpen().To(Canceled).Confirm(Texts.CancelConfirm).Do(PostAsync);
}
