namespace DemoEcom.SalesInvoice;

/// <summary>
/// The active tenant's sales invoices, the newest first.
/// </summary>
public sealed class EcomSalesInvoiceGridService(EcomDbContext db, EcomSalesInvoiceFlowService flow, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomDocTypeData.SalesInvoiceId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSalesInvoiceFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
    columns.Add(r => r.PaidAmount).Label("Paid").AsMoney();
  }

  protected override void SetActions(Actions actions)
  {
    _ = EcomDocTexts.Of(L);
    actions.Add("CreateSalesReturn")
      .Allow(Ecom.SalesReturn.Edit)
      .Label("Create sales return")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.EnterAsync<EcomSalesReturnFormService>(
        db,
        ctx,
        EcomDocTypeData.SalesReturnId,
        EcomDocStatusData.SalesReturnDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));

    actions.Add("CreateIncomingPayment")
      .Allow(Ecom.IncomingPayment.Edit)
      .Label("Create incoming payment")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.PayAsync<EcomIncomingPaymentFormService>(
        db,
        ctx,
        EcomDocTypeData.IncomingPaymentId,
        EcomDocStatusData.IncomingPaymentDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));
  }
}
