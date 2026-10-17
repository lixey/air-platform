namespace DemoEcom.Migrations.Seeds;

/// <summary>
/// A warehouse with its organisation and that organisation's currency, the products it trades in and the suppliers of
/// those products.
/// </summary>
internal sealed record EcomStockShelf(
  long WarehouseId,
  long OrganizationId,
  long CurrencyId,
  EcomStockProduct[] Assortment,
  EcomStockOffer[] Offers
);
