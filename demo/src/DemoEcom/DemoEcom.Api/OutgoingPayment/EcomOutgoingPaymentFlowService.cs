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
