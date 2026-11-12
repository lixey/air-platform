namespace DemoEcom.Migrations.Seeds;

public static class EcomCodeRows
{
  public static EcomDocType[] DocTypes() =>
  [
    new() { Id = EcomDocTypeData.CustomerOrderId, Handle = EcomDocTypeData.CustomerOrder, Name = "Customer order", Prefix = "CO", SortOrder = 1, IsActive = true },
    new() { Id = EcomDocTypeData.PurchaseOrderId, Handle = EcomDocTypeData.PurchaseOrder, Name = "Purchase order", Prefix = "PO", SortOrder = 2, IsActive = true },
    new() { Id = EcomDocTypeData.SalesInvoiceId, Handle = EcomDocTypeData.SalesInvoice, Name = "Sales invoice", Prefix = "SI", SortOrder = 3, IsActive = true },
    new() { Id = EcomDocTypeData.SalesReturnId, Handle = EcomDocTypeData.SalesReturn, Name = "Sales return", Prefix = "SR", SortOrder = 4, IsActive = true },
    new() { Id = EcomDocTypeData.SupplierInvoiceId, Handle = EcomDocTypeData.SupplierInvoice, Name = "Supplier invoice", Prefix = "PI", SortOrder = 5, IsActive = true },
    new() { Id = EcomDocTypeData.SupplierReturnId, Handle = EcomDocTypeData.SupplierReturn, Name = "Supplier return", Prefix = "PR", SortOrder = 6, IsActive = true },
    new() { Id = EcomDocTypeData.IncomingPaymentId, Handle = EcomDocTypeData.IncomingPayment, Name = "Incoming payment", Prefix = "IP", SortOrder = 7, IsActive = true },
    new() { Id = EcomDocTypeData.OutgoingPaymentId, Handle = EcomDocTypeData.OutgoingPayment, Name = "Outgoing payment", Prefix = "OP", SortOrder = 8, IsActive = true },
    new() { Id = EcomDocTypeData.StockTransferId, Handle = EcomDocTypeData.StockTransfer, Name = "Stock transfer", Prefix = "ST", SortOrder = 9, IsActive = true },
    new() { Id = EcomDocTypeData.StockReceiptId, Handle = EcomDocTypeData.StockReceipt, Name = "Stock receipt", Prefix = "RC", SortOrder = 10, IsActive = true },
    new() { Id = EcomDocTypeData.StockIssueId, Handle = EcomDocTypeData.StockIssue, Name = "Stock issue", Prefix = "IS", SortOrder = 11, IsActive = true }
  ];

  public static EcomDocStatus[] DocStatuses()
  {
    var colors = AppCodeRows.Colors().ToDictionary(color => color.Handle, color => color.Id, StringComparer.OrdinalIgnoreCase);
    return
    [
      Row(EcomDocStatusData.CustomerOrderNewId, EcomDocStatusData.CustomerOrderNew, "New", UiColor.Sky, editable: true),
      Row(EcomDocStatusData.CustomerOrderInProgressId, EcomDocStatusData.CustomerOrderInProgress, "In progress", UiColor.Amber, editable: true),
      Row(EcomDocStatusData.CustomerOrderConfirmedId, EcomDocStatusData.CustomerOrderConfirmed, "Confirmed", UiColor.Blue, posted: true),
      Row(EcomDocStatusData.CustomerOrderPausedId, EcomDocStatusData.CustomerOrderPaused, "Paused", UiColor.Amber, posted: true),
      Row(EcomDocStatusData.CustomerOrderCompletedId, EcomDocStatusData.CustomerOrderCompleted, "Completed", UiColor.Green, posted: true, final: true),
      Row(EcomDocStatusData.CustomerOrderCanceledId, EcomDocStatusData.CustomerOrderCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.PurchaseOrderNewId, EcomDocStatusData.PurchaseOrderNew, "New", UiColor.Sky, editable: true),
      Row(EcomDocStatusData.PurchaseOrderSentId, EcomDocStatusData.PurchaseOrderSent, "Sent", UiColor.Blue, posted: true),
      Row(EcomDocStatusData.PurchaseOrderCompletedId, EcomDocStatusData.PurchaseOrderCompleted, "Completed", UiColor.Green, posted: true, final: true),
      Row(EcomDocStatusData.PurchaseOrderCanceledId, EcomDocStatusData.PurchaseOrderCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.SalesInvoiceDraftId, EcomDocStatusData.SalesInvoiceDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.SalesInvoiceReadyToShipId, EcomDocStatusData.SalesInvoiceReadyToShip, "Ready to ship", UiColor.Blue),
      Row(EcomDocStatusData.SalesInvoiceInTransitId, EcomDocStatusData.SalesInvoiceInTransit, "In transit", UiColor.Amber, posted: true),
      Row(EcomDocStatusData.SalesInvoiceDeliveredId, EcomDocStatusData.SalesInvoiceDelivered, "Delivered", UiColor.Green, posted: true, final: true),
      Row(EcomDocStatusData.SalesInvoiceCanceledId, EcomDocStatusData.SalesInvoiceCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.SalesReturnDraftId, EcomDocStatusData.SalesReturnDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.SalesReturnPostedId, EcomDocStatusData.SalesReturnPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomDocStatusData.SalesReturnCanceledId, EcomDocStatusData.SalesReturnCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.SupplierInvoiceDraftId, EcomDocStatusData.SupplierInvoiceDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.SupplierInvoicePostedId, EcomDocStatusData.SupplierInvoicePosted, "Posted", UiColor.Green, posted: true),
      Row(EcomDocStatusData.SupplierInvoiceCanceledId, EcomDocStatusData.SupplierInvoiceCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.SupplierReturnDraftId, EcomDocStatusData.SupplierReturnDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.SupplierReturnPostedId, EcomDocStatusData.SupplierReturnPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomDocStatusData.SupplierReturnCanceledId, EcomDocStatusData.SupplierReturnCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.IncomingPaymentDraftId, EcomDocStatusData.IncomingPaymentDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.IncomingPaymentPostedId, EcomDocStatusData.IncomingPaymentPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomDocStatusData.IncomingPaymentCanceledId, EcomDocStatusData.IncomingPaymentCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.OutgoingPaymentDraftId, EcomDocStatusData.OutgoingPaymentDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.OutgoingPaymentPostedId, EcomDocStatusData.OutgoingPaymentPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomDocStatusData.OutgoingPaymentCanceledId, EcomDocStatusData.OutgoingPaymentCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.StockTransferDraftId, EcomDocStatusData.StockTransferDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.StockTransferPostedId, EcomDocStatusData.StockTransferPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomDocStatusData.StockTransferCanceledId, EcomDocStatusData.StockTransferCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.StockReceiptDraftId, EcomDocStatusData.StockReceiptDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.StockReceiptPostedId, EcomDocStatusData.StockReceiptPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomDocStatusData.StockReceiptCanceledId, EcomDocStatusData.StockReceiptCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomDocStatusData.StockIssueDraftId, EcomDocStatusData.StockIssueDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomDocStatusData.StockIssuePostedId, EcomDocStatusData.StockIssuePosted, "Posted", UiColor.Green, posted: true),
      Row(EcomDocStatusData.StockIssueCanceledId, EcomDocStatusData.StockIssueCanceled, "Canceled", UiColor.Red, final: true, canceled: true)
    ];

    EcomDocStatus Row(
      long id,
      string handle,
      string name,
      string hue,
      bool editable = false,
      bool posted = false,
      bool final = false,
      bool canceled = false
    ) =>
      new()
      {
        Id = id,
        Handle = handle,
        Name = name,
        DocTypeId = id / 100,
        ColorId = colors[hue],
        SortOrder = (int)(id % 100) * 10,
        IsEditable = editable,
        IsPosted = posted,
        IsFinal = final,
        IsCanceled = canceled,
        IsActive = true
      };
  }

  public static async Task SyncAsync(EcomDbContext db, CancellationToken cancellationToken = default)
  {
    await CodeRows.SyncAsync(db, DocTypes(), cancellationToken);
    await CodeRows.SyncAsync(db, DocStatuses(), cancellationToken);
  }
}
