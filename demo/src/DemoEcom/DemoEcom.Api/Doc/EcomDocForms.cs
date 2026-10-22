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
