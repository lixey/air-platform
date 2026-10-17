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

[Module("org", Name = "Organisation")]
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

  public static readonly Feature OrganizationForm = Feature<OrgOrganizationFormService>();
  public static readonly Feature WarehouseForm = Feature<OrgWarehouseFormService>();
  public static readonly Feature MoneyAccountForm = Feature<OrgMoneyAccountFormService>();
  public static readonly Feature CustomerForm = Feature<OrgCustomerFormService>();
  public static readonly Feature CustomerContactForm = Feature<OrgCustomerContactFormService>();
  public static readonly Feature CounterpartyForm = Feature<OrgCounterpartyFormService>();
  public static readonly Feature ProductForm = Feature<OrgProductFormService>();
  public static readonly Feature BrandForm = Feature<OrgBrandFormService>();
  public static readonly Feature ProductCategoryForm = Feature<OrgProductCategoryFormService>();
  public static readonly Feature DeliveryTypeForm = Feature<OrgDeliveryTypeFormService>();
  public static readonly Feature PaymentMethodForm = Feature<OrgPaymentMethodFormService>();
  public static readonly Feature PaymentTermForm = Feature<OrgPaymentTermFormService>().ReadOnly();
  public static readonly Feature CurrencyForm = Feature<OrgCurrencyFormService>().ReadOnly();
  public static readonly Feature UnitForm = Feature<OrgUnitFormService>().ReadOnly();
}
