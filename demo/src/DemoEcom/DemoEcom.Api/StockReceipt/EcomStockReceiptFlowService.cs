namespace DemoEcom.StockReceipt;

/// <summary>
/// The status flow of a stock receipt.
/// </summary>
public sealed class EcomStockReceiptFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.StockReceiptDraftId;
  private const long Posted = EcomDocStatusData.StockReceiptPostedId;
  private const long Canceled = EcomDocStatusData.StockReceiptCanceledId;

  protected override long Type => EcomDocTypeData.StockReceiptId;

  public override Feature Feature => Ecom.StockReceipt;

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
