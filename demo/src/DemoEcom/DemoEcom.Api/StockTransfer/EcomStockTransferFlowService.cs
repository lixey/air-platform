namespace DemoEcom.StockTransfer;

/// <summary>
/// The status flow of a stock transfer.
/// </summary>
public sealed class EcomStockTransferFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.StockTransferDraftId;
  private const long Posted = EcomData.DocStatus.StockTransferPostedId;
  private const long Canceled = EcomData.DocStatus.StockTransferCanceledId;

  protected override long Type => EcomData.DocType.StockTransferId;

  public override Feature Feature => Ecom.StockTransfer;

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
