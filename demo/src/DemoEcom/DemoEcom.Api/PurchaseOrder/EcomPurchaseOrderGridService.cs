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

  protected override void SetQuery(Query query)
  {
    base.SetQuery(query);
    var me = User?.UserId;
    query.NamedFilter("status", L["Status"])
      .ShowCounts()
      .Item(
        "open",
        L["In work"],
        doc => doc.DocStatusId == EcomData.DocStatus.PurchaseOrderNewId || doc.DocStatusId == EcomData.DocStatus.PurchaseOrderSentId
      ).Color(UiColor.Amber).Default()
      .Item(
        "closed",
        L["Finished"],
        doc => doc.DocStatusId == EcomData.DocStatus.PurchaseOrderCompletedId || doc.DocStatusId == EcomData.DocStatus.PurchaseOrderCanceledId
      ).Color(UiColor.Emerald)
      .All(L["All"]);
    query.NamedFilter("mine", L["Mine"], doc => doc.ManagerId == me).ShowCount().Remember();
  }

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomPurchaseOrderFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.DueDate);
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
    columns.Add(r => r.PaidAmount).Label("Paid").AsMoney();
  }

  protected override void SetActions(Actions actions)
  {
    _ = EcomDocTexts.Of(L);
    actions.Add("CreateSupplierInvoice")
      .Allow(Ecom.SupplierInvoice.Edit)
      .Label("Create supplier invoice")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => EcomDocActions.EnterAsync<EcomSupplierInvoiceFormService>(
        db,
        ctx,
        EcomData.DocType.SupplierInvoiceId,
        EcomData.DocStatus.SupplierInvoiceDraftId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));

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
}
