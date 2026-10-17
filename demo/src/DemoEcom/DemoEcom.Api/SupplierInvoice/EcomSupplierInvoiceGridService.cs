namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.SupplierInvoiceId;

  public override Feature Feature => Ecom.SupplierInvoice;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSupplierInvoiceFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetNav(Nav nav)
  {
    nav.AddAction(CreateOutgoingPayment, b => b.Label("Create outgoing payment").Icon(UiIcon.Plus));
  }

  public Task<GridActionDto> CreateOutgoingPayment(GridRequest request, CancellationToken cancellationToken) =>
    ExecuteBatchAsync(request, (context, ct) =>
    {
      foreach (var inv in context.Records)
      {
        var payment = new EcomDoc
        {
          DocTypeId = EcomData.DocType.OutgoingPaymentId,
          Date = DateOnly.FromDateTime(DateTime.UtcNow),
          OrganizationId = inv.OrganizationId,
          CounterpartyId = inv.CounterpartyId,
          Amount = inv.Amount,
          Note = $"Payment for supplier invoice {inv.Number}"
        };
        db.Add(payment);
      }
      return Task.CompletedTask;
    }, cancellationToken);
}
