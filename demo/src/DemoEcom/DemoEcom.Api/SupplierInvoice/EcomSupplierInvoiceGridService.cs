namespace DemoEcom.SupplierInvoice;

/// <summary>
/// The active tenant's supplier invoices, the newest first.
/// </summary>
public sealed class EcomSupplierInvoiceGridService(
  EcomDbContext db, EcomSupplierInvoiceFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomDocTypeData.SupplierInvoiceId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SupplierInvoice;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomSupplierInvoiceFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.ExternalNumber);
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
    Columns.Add(r => r.PaidAmount).Label("Paid").AsMoney();
  }

  protected override void SetActions()
  {
    _ = EcomDocTexts.Of(L);
    Actions.Add("CreateSupplierReturn")
      .Allow(Ecom.SupplierReturn.Edit)
      .Label("Create supplier return")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.EnterAsync<EcomSupplierReturnFormService>(
        db,
        ctx,
        EcomDocTypeData.SupplierReturnId,
        EcomDocStatusData.SupplierReturnDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));

    Actions.Add("CreateOutgoingPayment")
      .Allow(Ecom.OutgoingPayment.Edit)
      .Label("Create outgoing payment")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.PayAsync<EcomOutgoingPaymentFormService>(
        db,
        ctx,
        EcomDocTypeData.OutgoingPaymentId,
        EcomDocStatusData.OutgoingPaymentDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));
  }
}
