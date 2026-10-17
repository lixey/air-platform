using DemoEcom.Data;
using DemoEcom.Documents;
using DemoEcom.OutgoingPayment;
using DemoEcom.SupplierReturn;
using DemoOrg.Counterparty;

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
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.ExternalNumber);
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
    columns.Add(r => r.PaidAmount).Label("Paid").AsMoney().Sum();
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(CreateSupplierReturn, b => b.Label(L["Create supplier return"]).Icon("plus"));
    nav.AddAction(CreateOutgoingPayment, b => b.Label(L["Create outgoing payment"]).Icon("plus"));
  }

  public async Task<GridActionDto> CreateSupplierReturn(GridRequest request, CancellationToken cancellationToken)
  {
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    return await ExecuteBatchAsync(
      request,
      (c, token) => EcomDocActions.EnterAsync<EcomSupplierReturnFormService>(
        db,
        c,
        EcomData.DocType.SupplierReturnId,
        EcomData.DocStatus.SupplierReturnDraftId,
        date,
        EcomDocTexts.Of(L),
        token
      ),
      cancellationToken
    );
  }

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
