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
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
    columns.Add(r => r.PaidAmount).Label("Paid").AsMoney().Sum();
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.BeforeDelete((context, ct) =>
      EcomDocActions.RefuseDeleteAsync(db, context, Type, EcomDocTexts.Of(L), ct));
  }

  protected override void SetNav(Nav nav)
  {
    _ = EcomDocTexts.Of(L);
    nav.AddAction(CreateSalesInvoice, b => b.Label(L["Create sales invoice"]).Icon(UiIcon.Plus));
    nav.AddAction(CreateIncomingPayment, b => b.Label(L["Create incoming payment"]).Icon(UiIcon.Plus));
  }

  public Task<GridActionDto> CreateSalesInvoice(GridRequest request, CancellationToken cancellationToken)
  {
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    return ExecuteBatchAsync(
      request,
      (context, ct) => EcomDocActions.EnterAsync<EcomSalesInvoiceFormService>(
        db,
        context,
        EcomData.DocType.SalesInvoiceId,
        EcomData.DocStatus.SalesInvoiceDraftId,
        date,
        EcomDocTexts.Of(L),
        ct
      ),
      cancellationToken
    );
  }

  public Task<GridActionDto> CreateIncomingPayment(GridRequest request, CancellationToken cancellationToken)
  {
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    return ExecuteBatchAsync(
      request,
      (context, ct) => EcomDocActions.PayAsync<EcomIncomingPaymentFormService>(
        db,
        context,
        EcomData.DocType.IncomingPaymentId,
        EcomData.DocStatus.IncomingPaymentDraftId,
        date,
        EcomDocTexts.Of(L),
        ct
      ),
      cancellationToken
    );
  }
}
