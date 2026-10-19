using DemoEcom.CustomerOrder;
using DemoEcom.Data;
using DemoEcom.IncomingPayment;
using DemoEcom.OutgoingPayment;
using DemoEcom.PurchaseOrder;
using DemoEcom.SalesInvoice;
using DemoEcom.SalesReturn;
using DemoEcom.StockIssue;
using DemoEcom.StockReceipt;
using DemoEcom.StockTransfer;
using DemoEcom.SupplierInvoice;
using DemoEcom.SupplierReturn;

namespace DemoEcom.Doc;

public static class EcomDocForms
{
  private static readonly Dictionary<long, Type> Forms = new()
  {
    [EcomData.DocType.CustomerOrderId] = typeof(EcomCustomerOrderFormService),
    [EcomData.DocType.PurchaseOrderId] = typeof(EcomPurchaseOrderFormService),
    [EcomData.DocType.SalesInvoiceId] = typeof(EcomSalesInvoiceFormService),
    [EcomData.DocType.SalesReturnId] = typeof(EcomSalesReturnFormService),
    [EcomData.DocType.SupplierInvoiceId] = typeof(EcomSupplierInvoiceFormService),
    [EcomData.DocType.SupplierReturnId] = typeof(EcomSupplierReturnFormService),
    [EcomData.DocType.IncomingPaymentId] = typeof(EcomIncomingPaymentFormService),
    [EcomData.DocType.OutgoingPaymentId] = typeof(EcomOutgoingPaymentFormService),
    [EcomData.DocType.StockTransferId] = typeof(EcomStockTransferFormService),
    [EcomData.DocType.StockReceiptId] = typeof(EcomStockReceiptFormService),
    [EcomData.DocType.StockIssueId] = typeof(EcomStockIssueFormService)
  };

  public static Type? Of(long docTypeId) => Forms.GetValueOrDefault(docTypeId);
}
