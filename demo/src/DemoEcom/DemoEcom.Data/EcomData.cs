namespace DemoEcom.Data;

public static class EcomData
{
  public const string ModuleHandle = "Ecom";

  /// <summary>The document types the code declares; an id and its handle are bound forever.</summary>
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
    public const string CustomerOrder = "CustomerOrder";
    public const string PurchaseOrder = "PurchaseOrder";
    public const string SalesInvoice = "SalesInvoice";
    public const string SalesReturn = "SalesReturn";
    public const string SupplierInvoice = "SupplierInvoice";
    public const string SupplierReturn = "SupplierReturn";
    public const string IncomingPayment = "IncomingPayment";
    public const string OutgoingPayment = "OutgoingPayment";
    public const string StockTransfer = "StockTransfer";
    public const string StockReceipt = "StockReceipt";
    public const string StockIssue = "StockIssue";
  }

  /// <summary>
  /// The document statuses the code declares: an id is its type's id times 100 plus the status's place among the type's,
  /// and a handle is the type's followed by the status's (<c>CustomerOrderConfirmed</c>, 103).
  /// </summary>
  public static class DocStatus
  {
    public const long CustomerOrderNewId = 101;
    public const long CustomerOrderInProgressId = 102;
    public const long CustomerOrderConfirmedId = 103;
    public const long CustomerOrderPausedId = 104;
    public const long CustomerOrderCompletedId = 105;
    public const long CustomerOrderCanceledId = 106;
    public const long PurchaseOrderNewId = 201;
    public const long PurchaseOrderSentId = 202;
    public const long PurchaseOrderCompletedId = 203;
    public const long PurchaseOrderCanceledId = 204;
    public const long SalesInvoiceDraftId = 301;
    public const long SalesInvoiceReadyToShipId = 302;
    public const long SalesInvoiceInTransitId = 303;
    public const long SalesInvoiceDeliveredId = 304;
    public const long SalesInvoiceCanceledId = 305;
    public const long SalesReturnDraftId = 401;
    public const long SalesReturnPostedId = 402;
    public const long SalesReturnCanceledId = 403;
    public const long SupplierInvoiceDraftId = 501;
    public const long SupplierInvoicePostedId = 502;
    public const long SupplierInvoiceCanceledId = 503;
    public const long SupplierReturnDraftId = 601;
    public const long SupplierReturnPostedId = 602;
    public const long SupplierReturnCanceledId = 603;
    public const long IncomingPaymentDraftId = 701;
    public const long IncomingPaymentPostedId = 702;
    public const long IncomingPaymentCanceledId = 703;
    public const long OutgoingPaymentDraftId = 801;
    public const long OutgoingPaymentPostedId = 802;
    public const long OutgoingPaymentCanceledId = 803;
    public const long StockTransferDraftId = 901;
    public const long StockTransferPostedId = 902;
    public const long StockTransferCanceledId = 903;
    public const long StockReceiptDraftId = 1001;
    public const long StockReceiptPostedId = 1002;
    public const long StockReceiptCanceledId = 1003;
    public const long StockIssueDraftId = 1101;
    public const long StockIssuePostedId = 1102;
    public const long StockIssueCanceledId = 1103;
    public const string CustomerOrderNew = "CustomerOrderNew";
    public const string CustomerOrderInProgress = "CustomerOrderInProgress";
    public const string CustomerOrderConfirmed = "CustomerOrderConfirmed";
    public const string CustomerOrderPaused = "CustomerOrderPaused";
    public const string CustomerOrderCompleted = "CustomerOrderCompleted";
    public const string CustomerOrderCanceled = "CustomerOrderCanceled";
    public const string PurchaseOrderNew = "PurchaseOrderNew";
    public const string PurchaseOrderSent = "PurchaseOrderSent";
    public const string PurchaseOrderCompleted = "PurchaseOrderCompleted";
    public const string PurchaseOrderCanceled = "PurchaseOrderCanceled";
    public const string SalesInvoiceDraft = "SalesInvoiceDraft";
    public const string SalesInvoiceReadyToShip = "SalesInvoiceReadyToShip";
    public const string SalesInvoiceInTransit = "SalesInvoiceInTransit";
    public const string SalesInvoiceDelivered = "SalesInvoiceDelivered";
    public const string SalesInvoiceCanceled = "SalesInvoiceCanceled";
    public const string SalesReturnDraft = "SalesReturnDraft";
    public const string SalesReturnPosted = "SalesReturnPosted";
    public const string SalesReturnCanceled = "SalesReturnCanceled";
    public const string SupplierInvoiceDraft = "SupplierInvoiceDraft";
    public const string SupplierInvoicePosted = "SupplierInvoicePosted";
    public const string SupplierInvoiceCanceled = "SupplierInvoiceCanceled";
    public const string SupplierReturnDraft = "SupplierReturnDraft";
    public const string SupplierReturnPosted = "SupplierReturnPosted";
    public const string SupplierReturnCanceled = "SupplierReturnCanceled";
    public const string IncomingPaymentDraft = "IncomingPaymentDraft";
    public const string IncomingPaymentPosted = "IncomingPaymentPosted";
    public const string IncomingPaymentCanceled = "IncomingPaymentCanceled";
    public const string OutgoingPaymentDraft = "OutgoingPaymentDraft";
    public const string OutgoingPaymentPosted = "OutgoingPaymentPosted";
    public const string OutgoingPaymentCanceled = "OutgoingPaymentCanceled";
    public const string StockTransferDraft = "StockTransferDraft";
    public const string StockTransferPosted = "StockTransferPosted";
    public const string StockTransferCanceled = "StockTransferCanceled";
    public const string StockReceiptDraft = "StockReceiptDraft";
    public const string StockReceiptPosted = "StockReceiptPosted";
    public const string StockReceiptCanceled = "StockReceiptCanceled";
    public const string StockIssueDraft = "StockIssueDraft";
    public const string StockIssuePosted = "StockIssuePosted";
    public const string StockIssueCanceled = "StockIssueCanceled";
  }
}
