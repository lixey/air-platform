namespace DemoEcom;

[Module(EcomData.ModuleHandle)]
public sealed class Ecom : ModuleDefinition<Ecom>
{
  public static readonly Feature CustomerOrder = Feature<EcomCustomerOrderGridService>().AllowAgent();
  public static readonly Feature SalesWorkspace = Feature<EcomSalesWorkspaceScreenService>(e =>
  {
    e.Name = "Sales Workspace";
    e.Label = "Sales Workspace";
  }).ReadOnly();
  public static readonly Feature SalesReport = Feature<EcomSalesReportService>(e =>
  {
    e.Name = "Sales Analytics";
    e.Label = "Sales Analytics";
  }).ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature SalesInvoice = Feature<EcomSalesInvoiceGridService>().AllowAgent();
  public static readonly Feature SalesReturn = Feature<EcomSalesReturnGridService>().AllowAgent();
  public static readonly Feature CustomerOrderFulfillment = Feature<EcomCustomerOrderFulfillmentGridService>(e =>
  {
    e.Name = "Customer Order Fulfillment";
    e.Label = "Customer Order Fulfillment";
  }).ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature PurchaseOrder = Feature<EcomPurchaseOrderGridService>().AllowAgent();
  public static readonly Feature SupplierInvoice = Feature<EcomSupplierInvoiceGridService>().AllowAgent();
  public static readonly Feature SupplierReturn = Feature<EcomSupplierReturnGridService>().AllowAgent();
  public static readonly Feature StockReceipt = Feature<EcomStockReceiptGridService>().AllowAgent();
  public static readonly Feature StockIssue = Feature<EcomStockIssueGridService>().AllowAgent();
  public static readonly Feature StockTransfer = Feature<EcomStockTransferGridService>().AllowAgent();
  public static readonly Feature StockBalance = Feature<EcomStockBalanceGridService>().ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature StockMove = Feature<EcomStockMoveGridService>().ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature IncomingPayment = Feature<EcomIncomingPaymentGridService>().AllowAgent();
  public static readonly Feature OutgoingPayment = Feature<EcomOutgoingPaymentGridService>().AllowAgent();
  public static readonly Feature MoneyBalance = Feature<EcomMoneyBalanceGridService>().ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature MoneyMove = Feature<EcomMoneyMoveGridService>().ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature Doc = Feature<Doc.EcomDocGridService>(e =>
  {
    e.Name = "Documents";
    e.Label = "Document";
  }).ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature DocType = Feature<EcomDocTypeGridService>(e =>
  {
    e.Name = "Document Types";
    e.Label = "Document Type";
  }).ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature DocStatus = Feature<EcomDocStatusGridService>(e =>
  {
    e.Name = "Document Statuses";
    e.Label = "Document Status";
  }).ReadOnly().AllowAgent(AgentAccess.Read);
}
