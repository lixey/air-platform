using Air.Data;
using DemoEcom.Data;
using DemoEcom.Data.Models;

namespace DemoEcom.Migrations.Seeds;

public static class EcomCodeRows
{
  public static EcomDocType[] DocTypes() =>
  [
    new() { Id = EcomData.DocType.CustomerOrderId, Handle = "CustomerOrder", Name = "Customer Order", IsActive = true },
    new() { Id = EcomData.DocType.PurchaseOrderId, Handle = "PurchaseOrder", Name = "Purchase Order", IsActive = true },
    new() { Id = EcomData.DocType.SalesInvoiceId, Handle = "SalesInvoice", Name = "Sales Invoice", IsActive = true },
    new() { Id = EcomData.DocType.SalesReturnId, Handle = "SalesReturn", Name = "Sales Return", IsActive = true },
    new() { Id = EcomData.DocType.SupplierInvoiceId, Handle = "SupplierInvoice", Name = "Supplier Invoice", IsActive = true },
    new() { Id = EcomData.DocType.SupplierReturnId, Handle = "SupplierReturn", Name = "Supplier Return", IsActive = true },
    new() { Id = EcomData.DocType.StockReceiptId, Handle = "StockReceipt", Name = "Stock Receipt", IsActive = true },
    new() { Id = EcomData.DocType.StockIssueId, Handle = "StockIssue", Name = "Stock Issue", IsActive = true },
    new() { Id = EcomData.DocType.StockTransferId, Handle = "StockTransfer", Name = "Stock Transfer", IsActive = true },
    new() { Id = EcomData.DocType.IncomingPaymentId, Handle = "IncomingPayment", Name = "Incoming Payment", IsActive = true },
    new() { Id = EcomData.DocType.OutgoingPaymentId, Handle = "OutgoingPayment", Name = "Outgoing Payment", IsActive = true }
  ];

  public static EcomDocStatus[] DocStatuses() =>
  [
    new() { Id = EcomData.DocStatus.CustomerOrderNewId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "New", Name = "New", ColorId = 1, IsActive = true },
    new() { Id = EcomData.DocStatus.CustomerOrderInProgressId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "InProgress", Name = "In Progress", ColorId = 2, IsActive = true },
    new() { Id = EcomData.DocStatus.CustomerOrderConfirmedId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Confirmed", Name = "Confirmed", ColorId = 5, IsActive = true },
    new() { Id = EcomData.DocStatus.CustomerOrderPausedId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Paused", Name = "Paused", ColorId = 6, IsActive = true },
    new() { Id = EcomData.DocStatus.CustomerOrderCompletedId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Completed", Name = "Completed", ColorId = 4, IsActive = true },
    new() { Id = EcomData.DocStatus.CustomerOrderCanceledId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Canceled", Name = "Canceled", ColorId = 7, IsActive = true }
  ];

  public static async Task SyncAsync(EcomDbContext db, CancellationToken cancellationToken = default)
  {
    await CodeRows.SyncAsync(db, DocTypes(), cancellationToken);
    await CodeRows.SyncAsync(db, DocStatuses(), cancellationToken);
  }
}
