using Air.App.Modules;
using DemoOrg.Brand;
using DemoOrg.Counterparty;
using DemoOrg.Currency;
using DemoOrg.Customer;
using DemoOrg.DeliveryType;
using DemoOrg.MoneyAccount;
using DemoOrg.Organization;
using DemoOrg.PaymentMethod;
using DemoOrg.PaymentTerm;
using DemoOrg.Product;
using DemoOrg.ProductCategory;
using DemoOrg.Unit;
using DemoOrg.Warehouse;

namespace DemoOrg;

[Module(OrgData.ModuleHandle)]
public sealed class Org : ModuleDefinition<Org>
{
  public static readonly Feature Organization = Feature<OrgOrganizationGridService>();
  public static readonly Feature Warehouse = Feature<OrgWarehouseGridService>();
  public static readonly Feature MoneyAccount = Feature<OrgMoneyAccountGridService>();
  public static readonly Feature Customer = Feature<OrgCustomerGridService>();
  public static readonly Feature Counterparty = Feature<OrgCounterpartyGridService>();
  public static readonly Feature Product = Feature<OrgProductGridService>();
  public static readonly Feature Brand = Feature<OrgBrandGridService>();
  public static readonly Feature ProductCategory = Feature<OrgProductCategoryGridService>();
  public static readonly Feature DeliveryType = Feature<OrgDeliveryTypeGridService>();
  public static readonly Feature PaymentMethod = Feature<OrgPaymentMethodGridService>();
  public static readonly Feature PaymentTerm = Feature<OrgPaymentTermGridService>().ReadOnly();
  public static readonly Feature Currency = Feature<OrgCurrencyGridService>().ReadOnly();
  public static readonly Feature Unit = Feature<OrgUnitGridService>().ReadOnly();
}

