using DemoEcom.Data;
using DemoEcom.Documents;
using DemoEcom.IncomingPayment;
using DemoEcom.SalesReturn;

namespace DemoEcom.SalesInvoice;

/// <summary>
/// The active tenant's sales invoices, the newest first.
/// </summary>
public sealed class EcomSalesInvoiceGridService(EcomDbContext db, EcomSalesInvoiceFlowService flow, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.SalesInvoiceId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSalesInvoiceFormService>();
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
    base.SetNav(nav);
    nav.AddAction(CreateSalesReturn, b => b.Label(L["Create sales return"]).Icon("plus")).InMenu();
    nav.AddAction(CreateIncomingPayment, b => b.Label(L["Create incoming payment"]).Icon("plus")).InMenu();
  }

  [RequireRight<EcomSalesReturnGridService>(EndpointAction.Edit)]
  public async Task<GridActionDto> CreateSalesReturn(GridRequest request, CancellationToken cancellationToken)
  {
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    return await ExecuteBatchAsync(
      request,
      (c, token) => EcomDocActions.EnterAsync<EcomSalesReturnFormService>(
        db,
        c,
        EcomData.DocType.SalesReturnId,
        EcomData.DocStatus.SalesReturnDraftId,
        date,
        EcomDocTexts.Of(L),
        token
      ),
      cancellationToken
    );
  }

  [RequireRight<EcomIncomingPaymentGridService>(EndpointAction.Edit)]
  public async Task<GridActionDto> CreateIncomingPayment(GridRequest request, CancellationToken cancellationToken)
  {
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    return await ExecuteBatchAsync(
      request,
      (c, token) => EcomDocActions.PayAsync<EcomIncomingPaymentFormService>(
        db,
        c,
        EcomData.DocType.IncomingPaymentId,
        EcomData.DocStatus.IncomingPaymentDraftId,
        date,
        EcomDocTexts.Of(L),
        token
      ),
      cancellationToken
    );
  }
}
