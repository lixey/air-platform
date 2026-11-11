namespace DemoEcom;

public sealed class EcomModule : PlatformModule<Ecom>
{
  private const string DomainKnowledge = """
    Demo E-Commerce & Warehouse ERP (Ecom + Org):
    - Organizations, warehouses, money accounts, customers, suppliers/counterparties, brands, categories, and products live in Org.
    - All trade, warehouse, and payment documents live in Ecom (CustomerOrder, SalesInvoice, SalesReturn, PurchaseOrder, SupplierInvoice, SupplierReturn, StockReceipt, StockIssue, StockTransfer, IncomingPayment, OutgoingPayment).
    - CustomerOrder tracks sales orders from New -> InProgress -> Confirmed -> Completed (or Canceled), reserves stock, and links to SalesInvoice (shipment) and IncomingPayment (payment).
    - PurchaseOrder tracks replenishment orders to suppliers and links to SupplierInvoice (receipt) and OutgoingPayment.
    - StockBalance and MoneyBalance show live inventory and cash balances; SalesReport provides KPI and monthly revenue analytics.
    """;

  protected override void ConfigureServices(WebApplicationBuilder builder)
  {
    builder.AddPlatformPostgres<EcomDbContext>();
    builder.Services.AddScoped<EcomService>();
    AgentMemoryCatalog.RegisterDomainKnowledge(
      EcomData.ModuleHandle,
      "Ecom Trade, Warehouse & Money Workflows",
      DomainKnowledge);
  }

  public override ModuleMenu Menu => ModuleMenu
    .Group(
      "Sales", "Sales",
      g => g.Route(Ecom.CustomerOrder).Route(Ecom.SalesWorkspace).Route(Ecom.SalesReport).Route(Ecom.SalesInvoice).Route(Ecom.SalesReturn))
    .Group(
      "Purchasing", "Purchasing",
      g => g.Route(Ecom.CustomerOrderFulfillment).Route(Ecom.PurchaseOrder).Route(Ecom.SupplierInvoice).Route(Ecom.SupplierReturn))
    .Group(
      "Warehouse", "Warehouse",
      g => g.Route(Ecom.StockReceipt).Route(Ecom.StockIssue).Route(Ecom.StockTransfer).Route(Ecom.StockBalance).Route(Ecom.StockMove))
    .Group(
      "Money", "Money",
      g => g.Route(Ecom.IncomingPayment).Route(Ecom.OutgoingPayment).Route(Ecom.MoneyBalance).Route(Ecom.MoneyMove))
    .Route(Ecom.Doc)
    .Group("ReferenceBooks", "Reference books", g => g.Route(Ecom.DocType).Route(Ecom.DocStatus))
    .Build();
}
