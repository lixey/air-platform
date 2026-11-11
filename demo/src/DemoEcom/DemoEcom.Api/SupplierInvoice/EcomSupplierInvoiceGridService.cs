namespace DemoEcom.SupplierInvoice;

/// <summary>
/// The active tenant's supplier invoices, the newest first.
/// </summary>
public sealed class EcomSupplierInvoiceGridService(
  EcomDbContext db, EcomSupplierInvoiceFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.SupplierInvoiceId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SupplierInvoice;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSupplierInvoiceFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.ExternalNumber);
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
    columns.Add(r => r.PaidAmount).Label("Paid").AsMoney();
  }

  protected override void SetActions(Actions actions)
  {
    _ = EcomDocTexts.Of(L);
    actions.Add("CreateSupplierReturn")
      .Allow(Ecom.SupplierReturn.Edit)
      .Label("Create supplier return")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.EnterAsync<EcomSupplierReturnFormService>(
        db,
        ctx,
        EcomData.DocType.SupplierReturnId,
        EcomData.DocStatus.SupplierReturnDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));

    actions.Add("CreateOutgoingPayment")
      .Allow(Ecom.OutgoingPayment.Edit)
      .Label("Create outgoing payment")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.PayAsync<EcomOutgoingPaymentFormService>(
        db,
        ctx,
        EcomData.DocType.OutgoingPaymentId,
        EcomData.DocStatus.OutgoingPaymentDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));
  }
}
