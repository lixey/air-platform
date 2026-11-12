namespace DemoEcom.Migrations.Seeds.Internals;

internal sealed record EcomExampleScope(
  long TenantId,
  Faker Faker,
  TimeZoneInfo Zone,
  string[] Notes,
  EcomExampleOrganization[] Organizations,
  EcomExampleCustomer[] Customers,
  Dictionary<long, EcomExampleContact> Contacts,
  Dictionary<long, EcomExampleProduct[]> Assortments,
  long[] Managers,
  long[] DeliveryTypes,
  HashSet<long> Pickups,
  long[] PaymentMethods,
  long[] PaymentTerms
);
