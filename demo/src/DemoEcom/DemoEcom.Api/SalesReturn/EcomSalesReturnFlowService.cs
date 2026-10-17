using DemoEcom.Data;
using DemoEcom.Documents;

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
