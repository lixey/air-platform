using DemoApp.Migrations.Seeds;
using DemoOrg.Data;
using DemoOrg.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoOrg.Migrations.Seeds;

public static class OrgExampleSeed
{
  public static async Task RunAsync(
    OrgDbContext db,
    OrgSeedVolume volume,
    IReadOnlyList<AppSeedTenant> tenants,
    CancellationToken cancellationToken = default
  )
  {
    foreach (var tenant in tenants)
    {
      var tenantId = tenant.Row.Id;

      var orgCount = Math.Max(1, volume.Organizations);
      var existingOrgs = await db.OrgOrganization.Where(o => o.TenantId == tenantId).CountAsync(cancellationToken);
      if (existingOrgs < orgCount)
      {
        var orgs = Enumerable.Range(existingOrgs + 1, orgCount - existingOrgs).Select(i => new OrgOrganization
        {
          TenantId = tenantId,
          Code = $"ORG-{tenantId}-{i:D2}",
          Name = i == 1 ? $"Central Enterprise ({tenant.Row.Name})" : $"Trading Division {i} ({tenant.Row.Name})",
          ColorId = i,
          CurrencyId = 1,
          IsActive = true
        }).ToList();

        db.OrgOrganization.AddRange(orgs);
        await db.SaveChangesAsync(cancellationToken);
      }

      var orgIds = await db.OrgOrganization.Where(o => o.TenantId == tenantId).Select(o => o.Id).ToListAsync(cancellationToken);

      var whCount = Math.Max(1, volume.Warehouses);
      var existingWhs = await db.OrgWarehouse.Where(w => w.TenantId == tenantId).CountAsync(cancellationToken);
      if (existingWhs < whCount)
      {
        var whs = Enumerable.Range(existingWhs + 1, whCount - existingWhs).Select(i => new OrgWarehouse
        {
          TenantId = tenantId,
          OrganizationId = orgIds[i % orgIds.Count],
          Name = i switch
          {
            1 => "Central Logistics Hub",
            2 => "Western Fulfillment Center",
            3 => "Southern Transit Terminal",
            _ => $"Distribution Warehouse #{i}"
          },
          Address = $"Logistics Park Way #{i * 10}",
          IsActive = true
        }).ToList();

        db.OrgWarehouse.AddRange(whs);
        await db.SaveChangesAsync(cancellationToken);
      }

      var accCount = Math.Max(1, volume.MoneyAccounts);
      var existingAccs = await db.OrgMoneyAccount.Where(a => a.TenantId == tenantId).CountAsync(cancellationToken);
      if (existingAccs < accCount)
      {
        var accs = Enumerable.Range(existingAccs + 1, accCount - existingAccs).Select(i => new OrgMoneyAccount
        {
          TenantId = tenantId,
          OrganizationId = orgIds[i % orgIds.Count],
          Name = $"Operating Account #{i}",
          CurrencyId = (i % 3) + 1,
          IsActive = true
        }).ToList();

        db.OrgMoneyAccount.AddRange(accs);
        await db.SaveChangesAsync(cancellationToken);
      }
    }
  }
}
