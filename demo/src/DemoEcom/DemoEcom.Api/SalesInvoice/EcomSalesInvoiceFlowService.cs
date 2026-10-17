using DemoEcom.Data;
using DemoEcom.Documents;

namespace DemoEcom.SalesInvoice;

/// <summary>
/// The status flow of a sales invoice.
/// </summary>
public sealed class EcomSalesInvoiceFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.SalesInvoiceDraftId;
  private const long ReadyToShip = EcomData.DocStatus.SalesInvoiceReadyToShipId;
  private const long InTransit = EcomData.DocStatus.SalesInvoiceInTransitId;
  private const long Delivered = EcomData.DocStatus.SalesInvoiceDeliveredId;
  private const long Canceled = EcomData.DocStatus.SalesInvoiceCanceledId;

  protected override long Type => EcomData.DocType.SalesInvoiceId;

  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetFlow(Flow<EcomDoc> flow)
  {
    base.SetFlow(flow);
    flow.Status(Draft, L["Draft"], UiColor.Slate);
    flow.Status(ReadyToShip, L["Ready to ship"], UiColor.Blue);
    flow.Status(InTransit, L["In transit"], UiColor.Amber);
    flow.Status(Delivered, L["Delivered"], UiColor.Green);
    flow.Status(Canceled, L["Canceled"], UiColor.Red);
    flow.Initial(Draft);
  }

  public Transition<EcomDoc> Reopen => From(ReadyToShip, InTransit).To(Draft).Do(PostAsync);

  public Transition<EcomDoc> Ready => From(Draft, InTransit).To(ReadyToShip).Do(PostAsync);

  public Transition<EcomDoc> Ship => From(Draft, ReadyToShip).To(InTransit).Do(PostAsync);

  public Transition<EcomDoc> Deliver => From(Draft, ReadyToShip, InTransit).To(Delivered).Do(PostAsync);

  public Transition<EcomDoc> Cancel => From(Draft, ReadyToShip, InTransit).To(Canceled).Confirm(Texts.CancelConfirm).Do(PostAsync);
}
