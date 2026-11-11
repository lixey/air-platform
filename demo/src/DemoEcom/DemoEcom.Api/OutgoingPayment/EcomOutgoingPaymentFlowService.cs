namespace DemoEcom.OutgoingPayment;

/// <summary>
/// The status flow of an outgoing payment.
/// </summary>
public sealed class EcomOutgoingPaymentFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.OutgoingPaymentDraftId;
  private const long Posted = EcomData.DocStatus.OutgoingPaymentPostedId;
  private const long Canceled = EcomData.DocStatus.OutgoingPaymentCanceledId;

  protected override long Type => EcomData.DocType.OutgoingPaymentId;

  public override Feature Feature => Ecom.OutgoingPayment;

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
