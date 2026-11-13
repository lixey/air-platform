namespace DemoEcom.PurchaseOrder;

/// <summary>
/// The active tenant's purchase orders, the newest first.
/// </summary>
public sealed class EcomPurchaseOrderGridService(EcomDbContext db, EcomPurchaseOrderFlowService flow, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomDocTypeData.PurchaseOrderId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetQuery()
  {
    base.SetQuery();
    var me = User?.UserId;
    Query.NamedFilter("Status", L["Status"])
      .ShowCounts()
      .Item(
        "Open",
        L["In work"],
        doc => doc.DocStatusId == EcomDocStatusData.PurchaseOrderNewId || doc.DocStatusId == EcomDocStatusData.PurchaseOrderSentId
      ).Color(UiColor.Amber).Default()
      .Item(
        "Closed",
        L["Finished"],
        doc => doc.DocStatusId == EcomDocStatusData.PurchaseOrderCompletedId || doc.DocStatusId == EcomDocStatusData.PurchaseOrderCanceledId
      ).Color(UiColor.Emerald)
      .All(L["All"]);
    Query.NamedFilter("Mine", L["Mine"], doc => doc.ManagerId == me).ShowCount().Remember();
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomPurchaseOrderFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.DueDate);
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
    Columns.Add(r => r.PaidAmount).Label("Paid").AsMoney();
  }

  protected override void SetActions()
  {
    _ = EcomDocTexts.Of(L);
    Actions.Add("CreateSupplierInvoice")
      .Allow(Ecom.SupplierInvoice.Edit)
      .Label("Create supplier invoice")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.EnterAsync<EcomSupplierInvoiceFormService>(
        db,
        ctx,
        EcomDocTypeData.SupplierInvoiceId,
        EcomDocStatusData.SupplierInvoiceDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));

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

  protected override void SetHooks()
  {
    base.SetHooks();
    Hooks.BeforeDelete(async (c, cancellationToken) =>
    {
      long[] orders = [.. c.Records.Select(doc => doc.Id)];
      var gone = await db.EcomDocLine.Where(line => orders.Contains(line.DocId)).Select(line => line.Id).ToListAsync(cancellationToken);
      await EcomDocPurchasing.PurchasedAsync(db, gone, gone, cancellationToken);
    });
  }
}
