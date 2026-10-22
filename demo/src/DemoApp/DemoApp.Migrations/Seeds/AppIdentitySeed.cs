namespace DemoApp.Migrations.Seeds;

public static class AppIdentitySeed
{
  public const string DefaultSeedPassword = "Password123!";

  public static async Task RunAsync(
    AppDbContext db,
    IReadOnlyList<AppSeedTenant> tenants,
    CancellationToken cancellationToken = default
  )
  {
    var passwordHash = new PasswordHasher().HashPassword(DefaultSeedPassword);

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
        new AppUser { TenantId = tenantId, Login = tenantId == 1 ? "admin" : $"admin{tenantId}", Name = $"Administrator ({tenant.Row.Name})", Email = $"admin@{tenant.Row.Handle.ToLowerInvariant()}.demo", PasswordHash = passwordHash, IsRoot = tenantId == 1, IsActive = true },
        new AppUser { TenantId = tenantId, Login = tenantId == 1 ? "olena.koval" : $"olena{tenantId}", Name = "Olena Koval", Email = $"olena@{tenant.Row.Handle.ToLowerInvariant()}.demo", PasswordHash = passwordHash, IsActive = true },
        new AppUser { TenantId = tenantId, Login = tenantId == 1 ? "dmytro.bond" : $"dmytro{tenantId}", Name = "Dmytro Bondarenko", Email = $"dmytro@{tenant.Row.Handle.ToLowerInvariant()}.demo", PasswordHash = passwordHash, IsActive = true },
        new AppUser { TenantId = tenantId, Login = tenantId == 1 ? "iryna.melnyk" : $"iryna{tenantId}", Name = "Iryna Melnyk", Email = $"iryna@{tenant.Row.Handle.ToLowerInvariant()}.demo", PasswordHash = passwordHash, IsActive = true }
      };

      foreach (var u in users)
      {
        var existing = await db.AppUser.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Login == u.Login, cancellationToken);
        if (existing == null)
        {
          db.AppUser.Add(u);
        }
        else
        {
          if (string.IsNullOrEmpty(existing.PasswordHash))
          {
            existing.PasswordHash = passwordHash;
          }

          if (u.IsRoot && !existing.IsRoot)
          {
            existing.IsRoot = true;
          }
        }
      }

      await db.SaveChangesAsync(cancellationToken);

      // Load roles and users for this tenant
      var dbRoles = await db.AppAuthRole.Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Name, cancellationToken);
      var dbUsers = await db.AppUser.Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Login, cancellationToken);

      // Seed grants
      if (dbRoles.TryGetValue("Administrator", out var adminRole))
      {
        await EnsureGrantAsync(db, adminRole.Id, "*", cancellationToken);
      }

      if (dbRoles.TryGetValue("Store Manager", out var managerRole))
      {
        string[] managerGrants =
        [
          "Ecom.*",
          "Org.*",
          "App.User.View", "App.User.MenuView",
          "App.Role.View", "App.Role.MenuView",
          "App.Change.View", "App.Change.MenuView",
          "App.Language.View", "App.Language.MenuView",
          "App.Color.View", "App.Color.MenuView"
        ];
        foreach (var g in managerGrants)
        {
          await EnsureGrantAsync(db, managerRole.Id, g, cancellationToken);
        }
      }

      if (dbRoles.TryGetValue("Read-Only Auditor", out var auditorRole))
      {
        string[] auditorGrants =
        [
          "*.View",
          "*.MenuView",
          "Ecom.CustomerOrder.View", "Ecom.CustomerOrder.MenuView",
          "Ecom.CustomerOrderLine.View", "Ecom.CustomerOrderLine.MenuView",
          "Ecom.Product.View", "Ecom.Product.MenuView",
          "Ecom.Customer.View", "Ecom.Customer.MenuView",
          "Org.Company.View", "Org.Company.MenuView",
          "Org.Department.View", "Org.Department.MenuView",
          "Org.Employee.View", "Org.Employee.MenuView",
          "App.User.View", "App.User.MenuView",
          "App.Role.View", "App.Role.MenuView",
          "App.Change.View", "App.Change.MenuView",
          "App.Language.View", "App.Language.MenuView",
          "App.Color.View", "App.Color.MenuView"
        ];
        foreach (var g in auditorGrants)
        {
          await EnsureGrantAsync(db, auditorRole.Id, g, cancellationToken);
        }
      }

      await db.SaveChangesAsync(cancellationToken);

      // Seed assignments
      var adminLogin = tenantId == 1 ? "admin" : $"admin{tenantId}";
      var olenaLogin = tenantId == 1 ? "olena.koval" : $"olena{tenantId}";
      var dmytroLogin = tenantId == 1 ? "dmytro.bond" : $"dmytro{tenantId}";
      var irynaLogin = tenantId == 1 ? "iryna.melnyk" : $"iryna{tenantId}";

      if (dbUsers.TryGetValue(adminLogin, out var adminUser) && dbRoles.TryGetValue("Administrator", out var rAdmin))
      {
        await EnsureAssignmentAsync(db, adminUser.Id, rAdmin.Id, cancellationToken);
      }

      if (dbUsers.TryGetValue(olenaLogin, out var olenaUser) && dbRoles.TryGetValue("Store Manager", out var rManager))
      {
        await EnsureAssignmentAsync(db, olenaUser.Id, rManager.Id, cancellationToken);
      }

      if (dbUsers.TryGetValue(dmytroLogin, out var dmytroUser) && dbRoles.TryGetValue("Read-Only Auditor", out var rAuditor))
      {
        await EnsureAssignmentAsync(db, dmytroUser.Id, rAuditor.Id, cancellationToken);
      }

      if (dbUsers.TryGetValue(irynaLogin, out var irynaUser) && dbRoles.TryGetValue("Store Manager", out var rManager2))
      {
        await EnsureAssignmentAsync(db, irynaUser.Id, rManager2.Id, cancellationToken);
      }

      await db.SaveChangesAsync(cancellationToken);
    }
  }

  private static async Task EnsureGrantAsync(AppDbContext db, long roleId, string permission, CancellationToken ct)
  {
    if (!await db.AppAuthGrant.AnyAsync(x => x.AuthRoleId == roleId && x.Permission == permission, ct))
    {
      db.AppAuthGrant.Add(new AppAuthGrant { AuthRoleId = roleId, Permission = permission });
    }
  }

  private static async Task EnsureAssignmentAsync(AppDbContext db, long userId, long roleId, CancellationToken ct)
  {
    if (!await db.AppAuthAssignment.AnyAsync(x => x.UserId == userId && x.AuthRoleId == roleId, ct))
    {
      db.AppAuthAssignment.Add(new AppAuthAssignment { UserId = userId, AuthRoleId = roleId, IsActive = true });
    }
  }
}
