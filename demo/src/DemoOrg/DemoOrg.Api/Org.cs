using Air.App.Modules;
using DemoOrg.Customer;
using DemoOrg.Product;
using DemoOrg.Warehouse;

namespace DemoOrg;

[Module("org", Name = "Organisation")]
public sealed class Org : ModuleDefinition<Org>
{
  public static readonly Feature Organization = Feature<OrgOrganizationGridService>();
  public static readonly Feature Warehouse = Feature<OrgWarehouseGridService>();
  public static readonly Feature Customer = Feature<OrgCustomerGridService>();
  public static readonly Feature Product = Feature<OrgProductGridService>();
  public static readonly Feature Brand = Feature<OrgBrandGridService>();
  public static readonly Feature ProductCategory = Feature<OrgProductCategoryGridService>();
  public static readonly Feature DeliveryType = Feature<OrgDeliveryTypeGridService>();
  public static readonly Feature PaymentMethod = Feature<OrgPaymentMethodGridService>();
  public static readonly Feature Currency = Feature<OrgCurrencyGridService>().ReadOnly();
  public static readonly Feature Unit = Feature<OrgUnitGridService>().ReadOnly();

  public static readonly Feature OrganizationForm = Feature<OrgOrganizationFormService>();
  public static readonly Feature WarehouseForm = Feature<OrgWarehouseFormService>();
  public static readonly Feature CustomerForm = Feature<OrgCustomerFormService>();
  public static readonly Feature ProductForm = Feature<OrgProductFormService>();
}
