namespace DemoEcom.StockIssue;

/// <summary>
/// The status flow of a stock issue.
/// </summary>
public sealed class EcomStockIssueFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.StockIssueDraftId;
  private const long Posted = EcomDocStatusData.StockIssuePostedId;
  private const long Canceled = EcomDocStatusData.StockIssueCanceledId;

  protected override long Type => EcomDocTypeData.StockIssueId;

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
