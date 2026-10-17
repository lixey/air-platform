namespace DemoEcom.Data.Documents;

/// <summary>What a stock balance is kept of: a product in a warehouse of an organisation.</summary>
public readonly record struct EcomStockKey(long OrganizationId, long WarehouseId, long ProductId);
