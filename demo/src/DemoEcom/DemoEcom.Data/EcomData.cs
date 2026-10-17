namespace DemoEcom.Data;

public static class EcomData
{
  public const string ModuleHandle = "ecom";

  public static class DocType
  {
    public const long CustomerOrderId = 1;
    public const long PurchaseOrderId = 2;
    public const long SalesInvoiceId = 3;
    public const long SalesReturnId = 4;
    public const long SupplierInvoiceId = 5;
    public const long SupplierReturnId = 6;
    public const long IncomingPaymentId = 7;
    public const long OutgoingPaymentId = 8;
    public const long StockTransferId = 9;
    public const long StockReceiptId = 10;
    public const long StockIssueId = 11;
  }

  public static class DocStatus
  {
    public const long CustomerOrderNewId = 101;
    public const long CustomerOrderInProgressId = 102;
    public const long CustomerOrderConfirmedId = 103;
    public const long CustomerOrderPausedId = 104;
    public const long CustomerOrderCompletedId = 105;
    public const long CustomerOrderCanceledId = 106;

    public const long SalesInvoiceDraftId = 301;
    public const long SalesInvoiceDeliveredId = 304;

    public const long StockReceiptDraftId = 1001;
    public const long StockReceiptPostedId = 1002;

    public const long PaymentDraftId = 701;
    public const long PaymentPostedId = 702;
  }
}
