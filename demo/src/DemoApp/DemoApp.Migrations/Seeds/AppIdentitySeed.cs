using DemoApp.Data;
using DemoApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoApp.Migrations.Seeds;

public static class AppIdentitySeed
{
  public static async Task RunAsync(
    AppDbContext db,
    IReadOnlyList<AppSeedTenant> tenants,
    CancellationToken cancellationToken = default
  )
  {
    foreach (var tenant in tenants)
    {
      var tenantId = tenant.Row.Id;

      var roles = new[]
      {
        new AppAuthRole { TenantId = tenantId, Name = "Administrator", Description = "Full system administration", IsActive = true },
        new AppAuthRole { TenantId = tenantId, Name = "Store Manager", Description = "Inventory and orders management", IsActive = true },
        new AppAuthRole { TenantId = tenantId, Name = "Read-Only Auditor", Description = "Reports and audit view only", IsActive = true }
      };

      foreach (var r in roles)
      {
        if (!await db.AppAuthRole.AnyAsync(x => x.TenantId == tenantId && x.Name == r.Name, cancellationToken))
        {
          db.AppAuthRole.Add(r);
        }
      }

      await db.SaveChangesAsync(cancellationToken);

      var users = new[]
      {
        new AppUser { TenantId = tenantId, Login = tenantId == 1 ? "admin" : $"admin{tenantId}", Name = $"Administrator ({tenant.Row.Name})", Email = $"admin@{tenant.Row.Handle.ToLowerInvariant()}.demo", IsActive = true },
        new AppUser { TenantId = tenantId, Login = tenantId == 1 ? "olena.koval" : $"olena{tenantId}", Name = "Olena Koval", Email = $"olena@{tenant.Row.Handle.ToLowerInvariant()}.demo", IsActive = true },
        new AppUser { TenantId = tenantId, Login = tenantId == 1 ? "dmytro.bond" : $"dmytro{tenantId}", Name = "Dmytro Bondarenko", Email = $"dmytro@{tenant.Row.Handle.ToLowerInvariant()}.demo", IsActive = true },
        new AppUser { TenantId = tenantId, Login = tenantId == 1 ? "iryna.melnyk" : $"iryna{tenantId}", Name = "Iryna Melnyk", Email = $"iryna@{tenant.Row.Handle.ToLowerInvariant()}.demo", IsActive = true }
      };

      foreach (var u in users)
      {
        if (!await db.AppUser.AnyAsync(x => x.TenantId == tenantId && x.Login == u.Login, cancellationToken))
        {
          db.AppUser.Add(u);
        }
      }

      await db.SaveChangesAsync(cancellationToken);
    }
  }
}
