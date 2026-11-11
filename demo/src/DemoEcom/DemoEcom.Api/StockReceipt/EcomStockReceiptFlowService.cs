namespace DemoEcom.StockReceipt;

/// <summary>
/// The status flow of a stock receipt.
/// </summary>
public sealed class EcomStockReceiptFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.StockReceiptDraftId;
  private const long Posted = EcomData.DocStatus.StockReceiptPostedId;
  private const long Canceled = EcomData.DocStatus.StockReceiptCanceledId;

  protected override long Type => EcomData.DocType.StockReceiptId;

  public override Feature Feature => Ecom.StockReceipt;

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
