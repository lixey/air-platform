namespace DemoEcom.PurchaseOrder;

/// <summary>
/// The status flow of a purchase order.
/// </summary>
public sealed class EcomPurchaseOrderFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long New = EcomData.DocStatus.PurchaseOrderNewId;
  private const long Sent = EcomData.DocStatus.PurchaseOrderSentId;
  private const long Completed = EcomData.DocStatus.PurchaseOrderCompletedId;
  private const long Canceled = EcomData.DocStatus.PurchaseOrderCanceledId;

  protected override long Type => EcomData.DocType.PurchaseOrderId;

  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetFlow(Flow<EcomDoc> flow)
  {
    base.SetFlow(flow);
    flow.Status(New, L["New"], UiColor.Sky);
    flow.Status(Sent, L["Sent"], UiColor.Blue);
    flow.Status(Completed, L["Completed"], UiColor.Green);
    flow.Status(Canceled, L["Canceled"], UiColor.Red);
    flow.Initial(New);
  }

  public Transition<EcomDoc> Reopen => From(Sent).To(New).Do(PostAsync);

  public Transition<EcomDoc> Send => From(New).To(Sent).Do(PostAsync);

  public Transition<EcomDoc> Complete => From(New, Sent).To(Completed).Do(PostAsync);

  public Transition<EcomDoc> Cancel => From(New, Sent).To(Canceled).Confirm(Texts.CancelConfirm).Do(PostAsync);
}
