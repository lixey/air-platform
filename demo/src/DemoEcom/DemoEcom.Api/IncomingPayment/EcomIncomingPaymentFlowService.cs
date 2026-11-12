namespace DemoEcom.IncomingPayment;

/// <summary>
/// The status flow of an incoming payment.
/// </summary>
public sealed class EcomIncomingPaymentFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.IncomingPaymentDraftId;
  private const long Posted = EcomDocStatusData.IncomingPaymentPostedId;
  private const long Canceled = EcomDocStatusData.IncomingPaymentCanceledId;

  protected override long Type => EcomDocTypeData.IncomingPaymentId;

  public override Feature Feature => Ecom.IncomingPayment;

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
