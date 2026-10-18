using Air.App.Metadata;
using Air.App.Modules;
using Air.Data.Postgres;
using DemoEcom.Data;
using Microsoft.AspNetCore.Builder;

namespace DemoEcom;

public sealed class EcomModule : PlatformModule<Ecom>
{
  protected override void ConfigureServices(WebApplicationBuilder builder)
  {
    builder.AddPlatformPostgres<EcomDbContext>();
    builder.Services.AddScoped<EcomService>();
  }

  public override ModuleMenu Menu => ModuleMenu
    .Group("Sales", "Sales", g => g.Route(Ecom.CustomerOrder).Route(Ecom.SalesInvoice).Route(Ecom.SalesReturn))
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
