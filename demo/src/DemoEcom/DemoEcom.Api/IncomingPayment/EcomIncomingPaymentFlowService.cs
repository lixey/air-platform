namespace DemoEcom.IncomingPayment;

/// <summary>
/// The status flow of an incoming payment.
/// </summary>
public sealed class EcomIncomingPaymentFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.IncomingPaymentDraftId;
  private const long Posted = EcomData.DocStatus.IncomingPaymentPostedId;
  private const long Canceled = EcomData.DocStatus.IncomingPaymentCanceledId;

  protected override long Type => EcomData.DocType.IncomingPaymentId;

  public override Feature Feature => Ecom.IncomingPayment;

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
