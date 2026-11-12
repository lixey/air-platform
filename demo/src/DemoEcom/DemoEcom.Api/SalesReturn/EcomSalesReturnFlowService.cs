namespace DemoEcom.SalesReturn;

/// <summary>
/// The status flow of a sales return.
/// </summary>
public sealed class EcomSalesReturnFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.SalesReturnDraftId;
  private const long Posted = EcomDocStatusData.SalesReturnPostedId;
  private const long Canceled = EcomDocStatusData.SalesReturnCanceledId;

  protected override long Type => EcomDocTypeData.SalesReturnId;

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
