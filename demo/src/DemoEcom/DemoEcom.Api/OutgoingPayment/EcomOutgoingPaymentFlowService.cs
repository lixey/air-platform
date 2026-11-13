namespace DemoEcom.OutgoingPayment;

/// <summary>
/// The status flow of an outgoing payment.
/// </summary>
public sealed class EcomOutgoingPaymentFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.OutgoingPaymentDraftId;
  private const long Posted = EcomDocStatusData.OutgoingPaymentPostedId;
  private const long Canceled = EcomDocStatusData.OutgoingPaymentCanceledId;

  protected override long Type => EcomDocTypeData.OutgoingPaymentId;

  public override Feature Feature => Ecom.OutgoingPayment;

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
