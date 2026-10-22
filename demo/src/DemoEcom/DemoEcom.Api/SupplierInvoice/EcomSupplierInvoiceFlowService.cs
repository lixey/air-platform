namespace DemoEcom.SupplierInvoice;

/// <summary>
/// The status flow of a supplier invoice.
/// </summary>
public sealed class EcomSupplierInvoiceFlowService(EcomDbContext db) : EcomDocFlowService(db)
{
  private const long Draft = EcomData.DocStatus.SupplierInvoiceDraftId;
  private const long Posted = EcomData.DocStatus.SupplierInvoicePostedId;
  private const long Canceled = EcomData.DocStatus.SupplierInvoiceCanceledId;

  protected override long Type => EcomData.DocType.SupplierInvoiceId;

  public override Feature Feature => Ecom.SupplierInvoice;

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
