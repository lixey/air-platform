namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderGridService(EcomDbContext db, EcomService? ecom = null, EcomCustomerOrderFlowService? flow = null)
  : EcomDocGridService(db, ecom, flow)
{
  protected override long Type => EcomData.DocType.CustomerOrderId;

  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomCustomerOrderFormService>();
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

  protected override void SetQuery(Query query)
  {
    base.SetQuery(query);
    query.InitialFilter(r => r.DocStatusId).In(
      EcomData.DocStatus.CustomerOrderNewId,
      EcomData.DocStatus.CustomerOrderInProgressId,
      EcomData.DocStatus.CustomerOrderConfirmedId,
      EcomData.DocStatus.CustomerOrderPausedId
    );
    query.SearchBy(r => r.Id, r => r.Number, r => r.Customer);
  }

  protected override void SetActions(Actions actions)
  {
    _ = EcomDocTexts.Of(L);
    actions.Add("CreateSalesInvoice")
      .Allow(Ecom.SalesInvoice.Edit)
      .Label("Create sales invoice")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.EnterAsync<EcomSalesInvoiceFormService>(
        db,
        ctx,
        EcomData.DocType.SalesInvoiceId,
        EcomData.DocStatus.SalesInvoiceDraftId,
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
        EcomData.DocType.IncomingPaymentId,
        EcomData.DocStatus.IncomingPaymentDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));
  }
}
