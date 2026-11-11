namespace DemoEcom.SalesReturn;

/// <summary>
/// The status flow of a sales return.
/// </summary>
public sealed class EcomSalesReturnFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.SalesReturnDraftId;
  private const long Posted = EcomData.DocStatus.SalesReturnPostedId;
  private const long Canceled = EcomData.DocStatus.SalesReturnCanceledId;

  protected override long Type => EcomData.DocType.SalesReturnId;

  public override Feature Feature => Ecom.SalesReturn;

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
