namespace DemoEcom.SalesInvoice;

/// <summary>
/// The status flow of a sales invoice.
/// </summary>
public sealed class EcomSalesInvoiceFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.SalesInvoiceDraftId;
  private const long ReadyToShip = EcomDocStatusData.SalesInvoiceReadyToShipId;
  private const long InTransit = EcomDocStatusData.SalesInvoiceInTransitId;
  private const long Delivered = EcomDocStatusData.SalesInvoiceDeliveredId;
  private const long Canceled = EcomDocStatusData.SalesInvoiceCanceledId;

  protected override long Type => EcomDocTypeData.SalesInvoiceId;

  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetFlow(Flow flow)
  {
    base.SetFlow(flow);
    flow.Status(Draft, "Draft", UiColor.Slate);
    flow.Status(ReadyToShip, "Ready to ship", UiColor.Blue);
    flow.Status(InTransit, "In transit", UiColor.Amber);
    flow.FinalStatus(Delivered, "Delivered", UiColor.Green);
    flow.FinalStatus(Canceled, "Canceled", UiColor.Red);
    flow.Initial(Draft);
  }

  public Transition Reopen => FromOpen().To(Draft).Do(PostAsync);

  public Transition Ready => FromOpen().To(ReadyToShip).Do(PostAsync);

  public Transition Ship => FromOpen().To(InTransit).Do(PostAsync);

  public Transition Deliver => FromOpen().To(Delivered).Do(PostAsync);

  public Transition Cancel => FromOpen().To(Canceled).Confirm(Texts.CancelConfirm).Do(PostAsync);
}
