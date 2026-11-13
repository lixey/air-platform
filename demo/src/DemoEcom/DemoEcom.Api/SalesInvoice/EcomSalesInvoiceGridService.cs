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

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomSalesInvoiceFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
    Columns.Add(r => r.PaidAmount).Label("Paid").AsMoney();
  }

  protected override void SetActions()
  {
    _ = EcomDocTexts.Of(L);
    Actions.Add("CreateSalesReturn")
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

    Actions.Add("CreateIncomingPayment")
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
