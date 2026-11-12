namespace DemoEcom.SupplierInvoice;

/// <summary>
/// The status flow of a supplier invoice.
/// </summary>
public sealed class EcomSupplierInvoiceFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomDocStatusData.SupplierInvoiceDraftId;
  private const long Posted = EcomDocStatusData.SupplierInvoicePostedId;
  private const long Canceled = EcomDocStatusData.SupplierInvoiceCanceledId;

  protected override long Type => EcomDocTypeData.SupplierInvoiceId;

  public override Feature Feature => Ecom.SupplierInvoice;

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
