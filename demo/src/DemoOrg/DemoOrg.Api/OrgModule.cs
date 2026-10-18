using Air.App.Metadata;
using Air.App.Modules;
using Air.Data.Postgres;
using DemoOrg.Data;
using Microsoft.AspNetCore.Builder;

namespace DemoOrg;

public sealed class OrgModule : PlatformModule<Org>
{
  protected override void ConfigureServices(WebApplicationBuilder builder)
  {
    builder.AddPlatformPostgres<OrgDbContext>();
    builder.Services.AddScoped<OrgService>();
  }

  public override ModuleMenu Menu => ModuleMenu
    .Route(Org.Organization)
    .Route(Org.Warehouse)
    .Route(Org.MoneyAccount)
    .Route(Org.Customer)
    .Route(Org.Counterparty)
    .Route(Org.Product)
    .Group(
      "ReferenceBooks",
      "Reference books",
      g => g.Route(Org.Brand)
        .Route(Org.ProductCategory)
        .Route(Org.DeliveryType)
        .Route(Org.PaymentMethod)
        .Route(Org.PaymentTerm)
        .Route(Org.Currency)
        .Route(Org.Unit)
    )
    .Build();
}
