namespace DemoEcom.PurchaseOrder;

/// <summary>
/// The status flow of a purchase order.
/// </summary>
public sealed class EcomPurchaseOrderFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long New = EcomDocStatusData.PurchaseOrderNewId;
  private const long Sent = EcomDocStatusData.PurchaseOrderSentId;
  private const long Completed = EcomDocStatusData.PurchaseOrderCompletedId;
  private const long Canceled = EcomDocStatusData.PurchaseOrderCanceledId;

  protected override long Type => EcomDocTypeData.PurchaseOrderId;

  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetFlow(Flow flow)
  {
    base.SetFlow(flow);
    flow.Status(New, "New", UiColor.Sky);
    flow.Status(Sent, "Sent", UiColor.Blue);
    flow.FinalStatus(Completed, "Completed", UiColor.Green);
    flow.FinalStatus(Canceled, "Canceled", UiColor.Red);
    flow.Initial(New);
  }

  public Transition Reopen => FromOpen().To(New).Do(PostAsync);

  public Transition Send => FromOpen().To(Sent).Do(PostAsync);

  public Transition Complete => FromOpen().To(Completed).Do(PostAsync);

  public Transition Cancel => FromOpen().To(Canceled).Confirm(Texts.CancelConfirm).Do(PostAsync);
}
