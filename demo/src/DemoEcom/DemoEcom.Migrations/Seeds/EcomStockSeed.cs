using DemoApp.Migrations.Seeds;
using DemoEcom.Data;
using DemoEcom.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Migrations.Seeds;

public static class EcomStockSeed
{
  public static async Task RunAsync(
    EcomDbContext db,
    IReadOnlyList<AppSeedTenant> tenants,
    CancellationToken cancellationToken = default
  )
  {
    foreach (var tenant in tenants)
    {
      var tenantId = tenant.Row.Id;
      var warehouses = await db.OrgWarehouse.Where(w => w.TenantId == tenantId).ToListAsync(cancellationToken);
      var products = await db.OrgProduct.Where(p => p.TenantId == tenantId).Take(100).ToListAsync(cancellationToken);

      if (warehouses.Count == 0 || products.Count == 0) continue;

      var existingBalances = await db.EcomStockBalance.Where(b => b.TenantId == tenantId).AnyAsync(cancellationToken);
      if (!existingBalances)
      {
        var balances = new List<EcomStockBalance>();
        foreach (var wh in warehouses)
        {
          foreach (var prod in products)
          {
            balances.Add(new EcomStockBalance
            {
              TenantId = tenantId,
              OrganizationId = wh.OrganizationId,
              WarehouseId = wh.Id,
              ProductId = prod.Id,
              Qty = 50,
              Reserved = 5,
              Expected = 20
            });
          }
        }

        db.EcomStockBalance.AddRange(balances);
        await db.SaveChangesAsync(cancellationToken);
      }
    }
  }
}
