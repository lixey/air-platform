namespace DemoEcom;

[Module(EcomData.ModuleHandle, Name = "E-Commerce")]
public sealed class Ecom : ModuleDefinition<Ecom>
{
  public static readonly Feature CustomerOrder = Feature<EcomCustomerOrderGridService>();
  public static readonly Feature SalesInvoice = Feature<EcomSalesInvoiceGridService>();
  public static readonly Feature SalesReturn = Feature<EcomSalesReturnGridService>();
  public static readonly Feature CustomerOrderFulfillment = Feature<EcomCustomerOrderFulfillmentGridService>(e =>
  {
    e.Name = "Customer Order Fulfillment";
    e.Label = "Customer Order Fulfillment";
  }).ReadOnly();
  public static readonly Feature PurchaseOrder = Feature<EcomPurchaseOrderGridService>();
  public static readonly Feature SupplierInvoice = Feature<EcomSupplierInvoiceGridService>();
  public static readonly Feature SupplierReturn = Feature<EcomSupplierReturnGridService>();
  public static readonly Feature StockReceipt = Feature<EcomStockReceiptGridService>();
  public static readonly Feature StockIssue = Feature<EcomStockIssueGridService>();
  public static readonly Feature StockTransfer = Feature<EcomStockTransferGridService>();
  public static readonly Feature StockBalance = Feature<EcomStockBalanceGridService>().ReadOnly();
  public static readonly Feature StockMove = Feature<EcomStockMoveGridService>().ReadOnly();
  public static readonly Feature IncomingPayment = Feature<EcomIncomingPaymentGridService>();
  public static readonly Feature OutgoingPayment = Feature<EcomOutgoingPaymentGridService>();
  public static readonly Feature MoneyBalance = Feature<EcomMoneyBalanceGridService>().ReadOnly();
  public static readonly Feature MoneyMove = Feature<EcomMoneyMoveGridService>().ReadOnly();
  public static readonly Feature Doc = Feature<Doc.EcomDocGridService>(e =>
  {
    e.Name = "Documents";
    e.Label = "Document";
  }).ReadOnly();
  public static readonly Feature DocType = Feature<EcomDocTypeGridService>(e =>
  {
    e.Name = "Document Types";
    e.Label = "Document Type";
  }).ReadOnly();
  public static readonly Feature DocStatus = Feature<EcomDocStatusGridService>(e =>
  {
    e.Name = "Document Statuses";
    e.Label = "Document Status";
  }).ReadOnly();
}
