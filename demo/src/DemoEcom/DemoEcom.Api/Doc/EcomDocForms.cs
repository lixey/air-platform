namespace DemoEcom.Doc;

public static class EcomDocForms
{
  private static readonly Dictionary<long, Type> Forms = new()
  {
    [EcomDocTypeData.CustomerOrderId] = typeof(EcomCustomerOrderFormService),
    [EcomDocTypeData.PurchaseOrderId] = typeof(EcomPurchaseOrderFormService),
    [EcomDocTypeData.SalesInvoiceId] = typeof(EcomSalesInvoiceFormService),
    [EcomDocTypeData.SalesReturnId] = typeof(EcomSalesReturnFormService),
    [EcomDocTypeData.SupplierInvoiceId] = typeof(EcomSupplierInvoiceFormService),
    [EcomDocTypeData.SupplierReturnId] = typeof(EcomSupplierReturnFormService),
    [EcomDocTypeData.IncomingPaymentId] = typeof(EcomIncomingPaymentFormService),
    [EcomDocTypeData.OutgoingPaymentId] = typeof(EcomOutgoingPaymentFormService),
    [EcomDocTypeData.StockTransferId] = typeof(EcomStockTransferFormService),
    [EcomDocTypeData.StockReceiptId] = typeof(EcomStockReceiptFormService),
    [EcomDocTypeData.StockIssueId] = typeof(EcomStockIssueFormService)
  };

  public static Type? Of(long docTypeId) => Forms.GetValueOrDefault(docTypeId);
}
