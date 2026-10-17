using DemoEcom.Data;
using DemoEcom.Documents;

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

  protected override void SetFlow(Flow<EcomDoc> flow)
  {
    base.SetFlow(flow);
    flow.Status(Draft, L["Draft"], UiColor.Slate);
    flow.Status(Posted, L["Posted"], UiColor.Green);
    flow.Status(Canceled, L["Canceled"], UiColor.Red);
    flow.Initial(Draft);
  }

  public Transition<EcomDoc> Reopen => From(Posted).To(Draft).Do(PostAsync);

  public Transition<EcomDoc> Post => From(Draft).To(Posted).Do(PostAsync);

  public Transition<EcomDoc> Cancel => From(Draft, Posted).To(Canceled).Confirm(Texts.CancelConfirm).Do(PostAsync);
}
