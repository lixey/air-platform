namespace DemoEcom.SupplierReturn;

/// <summary>
/// The status flow of a supplier return.
/// </summary>
public sealed class EcomSupplierReturnFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.SupplierReturnDraftId;
  private const long Posted = EcomDocStatusData.SupplierReturnPostedId;
  private const long Canceled = EcomDocStatusData.SupplierReturnCanceledId;

  protected override long Type => EcomDocTypeData.SupplierReturnId;

  public override Feature Feature => Ecom.SupplierReturn;

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
