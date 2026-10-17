namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.PurchaseOrderId;

  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomPurchaseOrderFormService>();
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
    nav.AddAction(CreateSupplierInvoice, b => b.Label("Create supplier invoice").Icon(UiIcon.Plus));
  }

  public Task<GridActionDto> CreateSupplierInvoice(GridRequest request, CancellationToken cancellationToken) =>
    ExecuteBatchAsync(request, (context, ct) =>
    {
      foreach (var order in context.Records)
      {
        var invoice = new EcomDoc
        {
          DocTypeId = EcomData.DocType.SupplierInvoiceId,
          Date = DateOnly.FromDateTime(DateTime.UtcNow),
          OrganizationId = order.OrganizationId,
          CounterpartyId = order.CounterpartyId,
          WarehouseId = order.WarehouseId,
          Amount = order.Amount,
          Qty = order.Qty,
          Note = $"Entered on purchase order {order.Number}"
        };
        db.Add(invoice);
      }
      return Task.CompletedTask;
    }, cancellationToken);
}
