namespace DemoEcom.Migrations.Seeds;

/// <summary>
/// What the seed writes one tenant's stock documents with: its key, the generator of its locale, the notes of its
/// language, its warehouses with their assortments, in the order of their keys, the warehouses of each organisation
/// that has more than one, its products by key, its users and the payment terms.
/// </summary>
internal sealed record EcomStockScope(
  long TenantId,
  Faker Faker,
  EcomStockTexts Texts,
  EcomStockShelf[] Shelves,
  EcomStockShelf[][] Neighbours,
  Dictionary<long, EcomStockProduct> Products,
  long[] Managers,
  long[] PaymentTerms
);
