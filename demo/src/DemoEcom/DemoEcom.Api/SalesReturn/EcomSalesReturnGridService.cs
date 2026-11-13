namespace DemoEcom.SalesReturn;

/// <summary>
/// The active tenant's sales returns, the newest first.
/// </summary>
public sealed class EcomSalesReturnGridService(EcomDbContext db, EcomSalesReturnFlowService flow, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomDocTypeData.SalesReturnId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomSalesReturnFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
    Columns.Add(r => r.PaidAmount).Label("Refunded").AsMoney();
  }

  protected override void SetActions()
  {
    _ = EcomDocTexts.Of(L);
    Actions.Add("CreateOutgoingPayment")
      .Allow(Ecom.OutgoingPayment.Edit)
      .Label("Create outgoing payment")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.PayAsync<EcomOutgoingPaymentFormService>(
        db,
        ctx,
        EcomDocTypeData.OutgoingPaymentId,
        EcomDocStatusData.OutgoingPaymentDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));
  }
}
