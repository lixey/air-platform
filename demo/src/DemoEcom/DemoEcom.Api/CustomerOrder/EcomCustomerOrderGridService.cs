namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
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

  protected override void SetNav(Nav nav)
  {
    nav.AddAction(CreateSalesInvoice, b => b.Label("Create sales invoice").Icon(UiIcon.Plus));
    nav.AddAction(CreateIncomingPayment, b => b.Label("Create incoming payment").Icon(UiIcon.Plus));
  }

  public Task<GridActionDto> CreateSalesInvoice(GridRequest request, CancellationToken cancellationToken) =>
    ExecuteBatchAsync(request, (context, ct) =>
    {
      foreach (var order in context.Records)
      {
        var invoice = new EcomDoc
        {
          DocTypeId = EcomData.DocType.SalesInvoiceId,
          DocStatusId = EcomData.DocStatus.SalesInvoiceDraftId,
          Date = DateOnly.FromDateTime(DateTime.UtcNow),
          OrganizationId = order.OrganizationId,
          CustomerId = order.CustomerId,
          WarehouseId = order.WarehouseId,
          Amount = order.Amount,
          Qty = order.Qty,
          Note = $"Entered on order {order.Number}"
        };
        db.Add(invoice);
      }
      return Task.CompletedTask;
    }, cancellationToken);

  public Task<GridActionDto> CreateIncomingPayment(GridRequest request, CancellationToken cancellationToken) =>
    ExecuteBatchAsync(request, (context, ct) =>
    {
      foreach (var order in context.Records)
      {
        var payment = new EcomDoc
        {
          DocTypeId = EcomData.DocType.IncomingPaymentId,
          DocStatusId = EcomData.DocStatus.PaymentDraftId,
          Date = DateOnly.FromDateTime(DateTime.UtcNow),
          OrganizationId = order.OrganizationId,
          CustomerId = order.CustomerId,
          Amount = order.Amount - order.PaidAmount,
          Note = $"Payment for order {order.Number}"
        };
        db.Add(payment);
      }
      return Task.CompletedTask;
    }, cancellationToken);
}
