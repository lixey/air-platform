namespace DemoEcom.StockTransfer;

/// <summary>
/// The status flow of a stock transfer.
/// </summary>
public sealed class EcomStockTransferFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.StockTransferDraftId;
  private const long Posted = EcomDocStatusData.StockTransferPostedId;
  private const long Canceled = EcomDocStatusData.StockTransferCanceledId;

  protected override long Type => EcomDocTypeData.StockTransferId;

  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetFlow()
  {
    base.SetFlow();
    Flow.Status(Draft, "Draft", UiColor.Slate);
    Flow.Status(Posted, "Posted", UiColor.Green);
    Flow.FinalStatus(Canceled, "Canceled", UiColor.Red);
    Flow.Initial(Draft);
  }

  public Transition Reopen => FromOpen().To(Draft).Do(PostAsync);

  public Transition Post => FromOpen().To(Posted).Do(PostAsync);

  public Transition Cancel => FromOpen().To(Canceled).Confirm(Texts.CancelConfirm).Do(PostAsync);
}
