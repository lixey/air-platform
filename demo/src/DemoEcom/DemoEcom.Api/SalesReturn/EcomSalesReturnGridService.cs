using DemoEcom.Data;
using DemoEcom.Documents;
using DemoEcom.OutgoingPayment;

namespace DemoEcom.SalesReturn;

/// <summary>
/// The active tenant's sales returns, the newest first.
/// </summary>
public sealed class EcomSalesReturnGridService(EcomDbContext db, EcomSalesReturnFlowService flow, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.SalesReturnId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSalesReturnFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
    columns.Add(r => r.PaidAmount).Label("Refunded").AsMoney().Sum();
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(CreateOutgoingPayment, b => b.Label(L["Create outgoing payment"]).Icon("plus")).InMenu();
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
