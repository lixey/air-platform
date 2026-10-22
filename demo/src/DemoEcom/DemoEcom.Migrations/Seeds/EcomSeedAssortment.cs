namespace DemoEcom.Migrations.Seeds;

internal static class EcomSeedAssortment
{
  public static async Task<Dictionary<long, long[]>> OfAsync(
    EcomDbContext db,
    long tenantId,
    int linesPerWarehouse,
    int seed,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    var warehouses = await db.OrgWarehouse.IgnoreQueryFilters()
      .Where(warehouse => warehouse.TenantId == tenantId && warehouse.IsActive && warehouse.Organization.IsActive)
      .OrderBy(warehouse => warehouse.Id).Select(warehouse => warehouse.Id).ToListAsync(cancellationToken);
    var products = await db.OrgProduct.IgnoreQueryFilters()
      .Where(product => product.TenantId == tenantId && product.IsActive && product.Supplier.IsSupplier && product.Supplier.IsActive)
      .OrderBy(product => product.Id).Select(product => product.Id).ToListAsync(cancellationToken);
    return Of(warehouses, products, linesPerWarehouse, seed);
  }

  public static Dictionary<long, long[]> Of(IReadOnlyList<long> warehouses, IReadOnlyList<long> products, int linesPerWarehouse, int seed)
  {
    ArgumentNullException.ThrowIfNull(warehouses);
    ArgumentNullException.ThrowIfNull(products);
    var random = new Randomizer(seed);
    var pool = products.ToArray();
    var count = Math.Clamp(linesPerWarehouse, 0, pool.Length);
    var assortment = new Dictionary<long, long[]>(warehouses.Count);
    foreach (var warehouse in warehouses)
    {
      for (var place = 0; place < count; place++)
      {
        var from = random.Int(place, pool.Length - 1);
        (pool[place], pool[from]) = (pool[from], pool[place]);
      }

      assortment[warehouse] = pool[..count];
    }

    return assortment;
  }
}
