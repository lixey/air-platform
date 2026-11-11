namespace DemoOrg;

public sealed class OrgModule : PlatformModule<Org>
{
  private const string DomainKnowledge = """
    Org Master Data & Catalog:
    - Organizations, Warehouses, and MoneyAccounts define legal entities, storage sites, and bank/cash accounts.
    - Customers, Counterparties (suppliers & legal buyers), DeliveryTypes, PaymentMethods, PaymentTerms, Currencies, and Units are shared master dictionaries.
    - Products reference Brand, ProductCategory, Unit, and optional default Supplier.
    """;

  protected override void ConfigureServices(WebApplicationBuilder builder)
  {
    builder.AddPlatformPostgres<OrgDbContext>();
    builder.Services.AddScoped<OrgService>();
    AgentMemoryCatalog.RegisterDomainKnowledge(OrgData.ModuleHandle, "Org Master Data & Catalog", DomainKnowledge);
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
