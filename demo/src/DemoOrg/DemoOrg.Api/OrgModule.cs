using Air.App.Metadata;
using Air.App.Modules;

namespace DemoOrg;

public sealed class OrgModule : PlatformModule<Org>
{
  public override ModuleMenu Menu => ModuleMenu
    .Route(Org.Organization)
    .Route(Org.Warehouse)
    .Route(Org.Customer)
    .Route(Org.Product)
    .Group(
      "ReferenceBooks",
      "Reference books",
      g => g.Route(Org.Brand).Route(Org.ProductCategory).Route(Org.DeliveryType).Route(Org.PaymentMethod)
        .Route(Org.Currency).Route(Org.Unit)
    )
    .Build();
}
