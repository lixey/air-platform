namespace DemoEcom.Migrations.Seeds;

/// <summary>A supplier with those of its products a warehouse trades in.</summary>
internal sealed record EcomStockOffer(EcomStockSupplier Supplier, EcomStockProduct[] Products);
