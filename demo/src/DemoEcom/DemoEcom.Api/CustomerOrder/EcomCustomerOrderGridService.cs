namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderGridService(EcomDbContext db, EcomService? ecom = null, EcomCustomerOrderFlowService? flow = null)
  : EcomDocGridService(db, ecom, flow)
{
  protected override long Type => EcomDocTypeData.CustomerOrderId;

  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomCustomerOrderFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
    Columns.Add(r => r.PaidAmount).Label("Paid").AsMoney();
  }

  protected override void SetQuery()
  {
    base.SetQuery();
    var me = User?.UserId;
    Query.NamedFilter("Status", L["Status"])
      .Inline()
      .ShowCounts()
      .Item(
        "Open",
        L["In work"],
        doc =>
          doc.DocStatusId == EcomDocStatusData.CustomerOrderNewId ||
          doc.DocStatusId == EcomDocStatusData.CustomerOrderInProgressId ||
          doc.DocStatusId == EcomDocStatusData.CustomerOrderConfirmedId ||
          doc.DocStatusId == EcomDocStatusData.CustomerOrderPausedId
      ).Color(UiColor.Amber).Default()
      .Item(
        "Completed",
        L["Completed"],
        doc => doc.DocStatusId == EcomDocStatusData.CustomerOrderCompletedId
      ).Color(UiColor.Emerald)
      .Item(
        "Canceled",
        L["Canceled"],
        doc => doc.DocStatusId == EcomDocStatusData.CustomerOrderCanceledId
      ).Color(UiColor.Rose)
      .All(L["All"]);
    Query.NamedFilter("Mine", L["Mine"], doc => doc.ManagerId == me).ShowCount().Remember();
    Query.SearchBy(r => r.Id, r => r.Number, r => r.Customer);
  }

  protected override void SetActions()
  {
    _ = EcomDocTexts.Of(L);
    Actions.Add("CreateSalesInvoice")
      .Allow(Ecom.SalesInvoice.Edit)
      .Label("Create sales invoice")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.EnterAsync<EcomSalesInvoiceFormService>(
        db,
        ctx,
        EcomDocTypeData.SalesInvoiceId,
        EcomDocStatusData.SalesInvoiceDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));

    Actions.Add("CreateIncomingPayment")
      .Allow(Ecom.IncomingPayment.Edit)
      .Label("Create incoming payment")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.PayAsync<EcomIncomingPaymentFormService>(
        db,
        ctx,
        EcomDocTypeData.IncomingPaymentId,
        EcomDocStatusData.IncomingPaymentDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));
  }
}
