namespace DemoEcom.SupplierReturn;

/// <summary>
/// The status flow of a supplier return.
/// </summary>
public sealed class EcomSupplierReturnFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.SupplierReturnDraftId;
  private const long Posted = EcomData.DocStatus.SupplierReturnPostedId;
  private const long Canceled = EcomData.DocStatus.SupplierReturnCanceledId;

  protected override long Type => EcomData.DocType.SupplierReturnId;

  public override Feature Feature => Ecom.SupplierReturn;

  protected override void SetFlow(Flow flow)
  {
    base.SetFlow(flow);
    flow.Status(Draft, L["Draft"], UiColor.Slate);
    flow.Status(Posted, L["Posted"], UiColor.Green);
    flow.Status(Canceled, L["Canceled"], UiColor.Red);
    flow.Initial(Draft);
  }

  public Transition Reopen => From(Posted).To(Draft).Do(PostAsync);

  public Transition Post => From(Draft).To(Posted).Do(PostAsync);

  public Transition Cancel => From(Draft, Posted).To(Canceled).Confirm(Texts.CancelConfirm).Do(PostAsync);
}
