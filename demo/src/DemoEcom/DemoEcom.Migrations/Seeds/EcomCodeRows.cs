using Air.App.Components;
using Air.Data;
using DemoApp.Migrations.Seeds;
using DemoEcom.Data;
using DemoEcom.Data.Models;

namespace DemoEcom.Migrations.Seeds;

public static class EcomCodeRows
{
  public static EcomDocType[] DocTypes() =>
  [
    new() { Id = EcomData.DocType.CustomerOrderId, Handle = EcomData.DocType.CustomerOrder, Name = "Customer order", Prefix = "CO", SortOrder = 1, IsActive = true },
    new() { Id = EcomData.DocType.PurchaseOrderId, Handle = EcomData.DocType.PurchaseOrder, Name = "Purchase order", Prefix = "PO", SortOrder = 2, IsActive = true },
    new() { Id = EcomData.DocType.SalesInvoiceId, Handle = EcomData.DocType.SalesInvoice, Name = "Sales invoice", Prefix = "SI", SortOrder = 3, IsActive = true },
    new() { Id = EcomData.DocType.SalesReturnId, Handle = EcomData.DocType.SalesReturn, Name = "Sales return", Prefix = "SR", SortOrder = 4, IsActive = true },
    new() { Id = EcomData.DocType.SupplierInvoiceId, Handle = EcomData.DocType.SupplierInvoice, Name = "Supplier invoice", Prefix = "PI", SortOrder = 5, IsActive = true },
    new() { Id = EcomData.DocType.SupplierReturnId, Handle = EcomData.DocType.SupplierReturn, Name = "Supplier return", Prefix = "PR", SortOrder = 6, IsActive = true },
    new() { Id = EcomData.DocType.IncomingPaymentId, Handle = EcomData.DocType.IncomingPayment, Name = "Incoming payment", Prefix = "IP", SortOrder = 7, IsActive = true },
    new() { Id = EcomData.DocType.OutgoingPaymentId, Handle = EcomData.DocType.OutgoingPayment, Name = "Outgoing payment", Prefix = "OP", SortOrder = 8, IsActive = true },
    new() { Id = EcomData.DocType.StockTransferId, Handle = EcomData.DocType.StockTransfer, Name = "Stock transfer", Prefix = "ST", SortOrder = 9, IsActive = true },
    new() { Id = EcomData.DocType.StockReceiptId, Handle = EcomData.DocType.StockReceipt, Name = "Stock receipt", Prefix = "RC", SortOrder = 10, IsActive = true },
    new() { Id = EcomData.DocType.StockIssueId, Handle = EcomData.DocType.StockIssue, Name = "Stock issue", Prefix = "IS", SortOrder = 11, IsActive = true }
  ];

  public static EcomDocStatus[] DocStatuses()
  {
    var colors = AppCodeRows.Colors().ToDictionary(color => color.Handle, color => color.Id);
    return
    [
      Row(EcomData.DocStatus.CustomerOrderNewId, EcomData.DocStatus.CustomerOrderNew, "New", UiColor.Sky, editable: true),
      Row(EcomData.DocStatus.CustomerOrderInProgressId, EcomData.DocStatus.CustomerOrderInProgress, "In progress", UiColor.Amber, editable: true),
      Row(EcomData.DocStatus.CustomerOrderConfirmedId, EcomData.DocStatus.CustomerOrderConfirmed, "Confirmed", UiColor.Blue, posted: true),
      Row(EcomData.DocStatus.CustomerOrderPausedId, EcomData.DocStatus.CustomerOrderPaused, "Paused", UiColor.Amber, posted: true),
      Row(EcomData.DocStatus.CustomerOrderCompletedId, EcomData.DocStatus.CustomerOrderCompleted, "Completed", UiColor.Green, posted: true, final: true),
      Row(EcomData.DocStatus.CustomerOrderCanceledId, EcomData.DocStatus.CustomerOrderCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.PurchaseOrderNewId, EcomData.DocStatus.PurchaseOrderNew, "New", UiColor.Sky, editable: true),
      Row(EcomData.DocStatus.PurchaseOrderSentId, EcomData.DocStatus.PurchaseOrderSent, "Sent", UiColor.Blue, posted: true),
      Row(EcomData.DocStatus.PurchaseOrderCompletedId, EcomData.DocStatus.PurchaseOrderCompleted, "Completed", UiColor.Green, posted: true, final: true),
      Row(EcomData.DocStatus.PurchaseOrderCanceledId, EcomData.DocStatus.PurchaseOrderCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.SalesInvoiceDraftId, EcomData.DocStatus.SalesInvoiceDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.SalesInvoiceReadyToShipId, EcomData.DocStatus.SalesInvoiceReadyToShip, "Ready to ship", UiColor.Blue),
      Row(EcomData.DocStatus.SalesInvoiceInTransitId, EcomData.DocStatus.SalesInvoiceInTransit, "In transit", UiColor.Amber, posted: true),
      Row(EcomData.DocStatus.SalesInvoiceDeliveredId, EcomData.DocStatus.SalesInvoiceDelivered, "Delivered", UiColor.Green, posted: true, final: true),
      Row(EcomData.DocStatus.SalesInvoiceCanceledId, EcomData.DocStatus.SalesInvoiceCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.SalesReturnDraftId, EcomData.DocStatus.SalesReturnDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.SalesReturnPostedId, EcomData.DocStatus.SalesReturnPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomData.DocStatus.SalesReturnCanceledId, EcomData.DocStatus.SalesReturnCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.SupplierInvoiceDraftId, EcomData.DocStatus.SupplierInvoiceDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.SupplierInvoicePostedId, EcomData.DocStatus.SupplierInvoicePosted, "Posted", UiColor.Green, posted: true),
      Row(EcomData.DocStatus.SupplierInvoiceCanceledId, EcomData.DocStatus.SupplierInvoiceCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.SupplierReturnDraftId, EcomData.DocStatus.SupplierReturnDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.SupplierReturnPostedId, EcomData.DocStatus.SupplierReturnPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomData.DocStatus.SupplierReturnCanceledId, EcomData.DocStatus.SupplierReturnCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.IncomingPaymentDraftId, EcomData.DocStatus.IncomingPaymentDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.IncomingPaymentPostedId, EcomData.DocStatus.IncomingPaymentPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomData.DocStatus.IncomingPaymentCanceledId, EcomData.DocStatus.IncomingPaymentCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.OutgoingPaymentDraftId, EcomData.DocStatus.OutgoingPaymentDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.OutgoingPaymentPostedId, EcomData.DocStatus.OutgoingPaymentPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomData.DocStatus.OutgoingPaymentCanceledId, EcomData.DocStatus.OutgoingPaymentCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.StockTransferDraftId, EcomData.DocStatus.StockTransferDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.StockTransferPostedId, EcomData.DocStatus.StockTransferPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomData.DocStatus.StockTransferCanceledId, EcomData.DocStatus.StockTransferCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.StockReceiptDraftId, EcomData.DocStatus.StockReceiptDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.StockReceiptPostedId, EcomData.DocStatus.StockReceiptPosted, "Posted", UiColor.Green, posted: true),
      Row(EcomData.DocStatus.StockReceiptCanceledId, EcomData.DocStatus.StockReceiptCanceled, "Canceled", UiColor.Red, final: true, canceled: true),
      Row(EcomData.DocStatus.StockIssueDraftId, EcomData.DocStatus.StockIssueDraft, "Draft", UiColor.Slate, editable: true),
      Row(EcomData.DocStatus.StockIssuePostedId, EcomData.DocStatus.StockIssuePosted, "Posted", UiColor.Green, posted: true),
      Row(EcomData.DocStatus.StockIssueCanceledId, EcomData.DocStatus.StockIssueCanceled, "Canceled", UiColor.Red, final: true, canceled: true)
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
