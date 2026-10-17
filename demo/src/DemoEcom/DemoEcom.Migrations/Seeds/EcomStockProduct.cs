namespace DemoEcom.Migrations.Seeds;

/// <summary>An active product of an active supplier: its key, its supplier and its purchase price.</summary>
internal sealed record EcomStockProduct(long Id, long SupplierId, decimal PurchasePrice);
