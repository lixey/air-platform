namespace DemoEcom.Documents;

/// <summary>The rules the document forms share; each answers whether the value passes, and the form gives the message.</summary>
public static class EcomDocRules
{
  public static bool WarehousesDiffer(long? warehouseId, long? warehouseToId) => warehouseToId is not { } to || warehouseId != to;

  public static async Task<bool> ProductOfAsync(EcomDbContext db, long? supplierId, long productId, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    return !await db.OrgProduct.AnyAsync(product => product.Id == productId && product.SupplierId != supplierId, cancellationToken);
  }

  public static async Task<bool> SupplierOfLinesAsync(EcomDbContext db, long docId, long? supplierId, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    return supplierId is not { } id ||
      !await db.EcomDocLine.AnyAsync(line => line.DocId == docId && line.Product.SupplierId != id, cancellationToken);
  }
}
