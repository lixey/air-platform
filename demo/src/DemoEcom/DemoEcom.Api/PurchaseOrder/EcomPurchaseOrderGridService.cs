using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Documents;
using DemoEcom.OutgoingPayment;
using DemoEcom.SupplierInvoice;
using DemoOrg.Counterparty;

namespace DemoEcom.PurchaseOrder;

/// <summary>
/// The active tenant's purchase orders, the newest first.
/// </summary>
public sealed class EcomPurchaseOrderGridService(EcomDbContext db, EcomPurchaseOrderFlowService flow, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.PurchaseOrderId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomPurchaseOrderFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.DueDate);
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
    columns.Add(r => r.PaidAmount).Label("Paid").AsMoney().Sum();
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(CreateSupplierInvoice, b => b.Label(L["Create supplier invoice"]).Icon("plus")).InMenu();
    nav.AddAction(CreateOutgoingPayment, b => b.Label(L["Create outgoing payment"]).Icon("plus")).InMenu();
  }

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.BeforeDelete(async (c, cancellationToken) =>
    {
      long[] orders = [.. c.Records.Select(doc => doc.Id)];
      var gone = await db.EcomDocLine.Where(line => orders.Contains(line.DocId)).Select(line => line.Id).ToListAsync(cancellationToken);
      await EcomDocEntering.PurchasedAsync(db, gone, gone, cancellationToken);
    });
  }

  [RequireRight<EcomSupplierInvoiceGridService>(EndpointAction.Edit)]
  public async Task<GridActionDto> CreateSupplierInvoice(GridRequest request, CancellationToken cancellationToken)
  {
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    return await ExecuteBatchAsync(
      request,
      (c, token) => EcomDocActions.EnterAsync<EcomSupplierInvoiceFormService>(
        db,
        c,
        EcomData.DocType.SupplierInvoiceId,
        EcomData.DocStatus.SupplierInvoiceDraftId,
        date,
        EcomDocTexts.Of(L),
        token
      ),
      cancellationToken
    );
  }

  [RequireRight<EcomOutgoingPaymentGridService>(EndpointAction.Edit)]
  public async Task<GridActionDto> CreateOutgoingPayment(GridRequest request, CancellationToken cancellationToken)
  {
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    return await ExecuteBatchAsync(
      request,
      (c, token) => EcomDocActions.PayAsync<EcomOutgoingPaymentFormService>(
        db,
        c,
        EcomData.DocType.OutgoingPaymentId,
        EcomData.DocStatus.OutgoingPaymentDraftId,
        date,
        EcomDocTexts.Of(L),
        token
      ),
      cancellationToken
    );
  }
}
