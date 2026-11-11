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
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
    columns.Add(r => r.PaidAmount).Label("Refunded").AsMoney();
  }

  protected override void SetActions(Actions actions)
  {
    _ = EcomDocTexts.Of(L);
    actions.Add("CreateOutgoingPayment")
      .Allow(Ecom.OutgoingPayment.Edit)
      .Label("Create outgoing payment")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.PayAsync<EcomOutgoingPaymentFormService>(
        db,
        ctx,
        EcomData.DocType.OutgoingPaymentId,
        EcomData.DocStatus.OutgoingPaymentDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));
  }
}
