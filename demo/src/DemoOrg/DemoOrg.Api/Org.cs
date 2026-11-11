namespace DemoOrg;

[Module(OrgData.ModuleHandle)]
public sealed class Org : ModuleDefinition<Org>
{
  public static readonly Feature Organization = Feature<OrgOrganizationGridService>().AllowAgent();
  public static readonly Feature Warehouse = Feature<OrgWarehouseGridService>().AllowAgent();
  public static readonly Feature MoneyAccount = Feature<OrgMoneyAccountGridService>().AllowAgent();
  public static readonly Feature Customer = Feature<OrgCustomerGridService>().AllowAgent();
  public static readonly Feature Counterparty = Feature<OrgCounterpartyGridService>().AllowAgent();
  public static readonly Feature Product = Feature<OrgProductGridService>().AllowAgent();
  public static readonly Feature Brand = Feature<OrgBrandGridService>().AllowAgent();
  public static readonly Feature ProductCategory = Feature<OrgProductCategoryGridService>().AllowAgent();
  public static readonly Feature DeliveryType = Feature<OrgDeliveryTypeGridService>().AllowAgent();
  public static readonly Feature PaymentMethod = Feature<OrgPaymentMethodGridService>().AllowAgent();
  public static readonly Feature PaymentTerm = Feature<OrgPaymentTermGridService>().ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature Currency = Feature<OrgCurrencyGridService>().ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature Unit = Feature<OrgUnitGridService>().ReadOnly().AllowAgent(AgentAccess.Read);
}

