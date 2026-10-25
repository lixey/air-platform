namespace Demo.DevMigrations;

public static class DevMigrationsRunner
{
  public static async Task<int> RunAsync(string[] args)
  {
    var action = args.Length > 0 ? args[0] : "prepare";
    if (action is not ("prepare" or "reset"))
    {
      Console.WriteLine("Usage: dotnet run --project demo/src/Demo.DevMigrations -- [prepare|reset] [--volume small|default|medium|large] [--seed integer]");
      return 1;
    }

    var volumeName = "default";
    var randomSeed = 1729;

    for (var i = 1; i < args.Length; i++)
    {
      if (args[i] == "--volume" && i + 1 < args.Length)
      {
        volumeName = args[++i];
      }
      else if (args[i] == "--seed" && i + 1 < args.Length && int.TryParse(args[++i], out var s))
      {
        randomSeed = s;
      }
    }

    var volume = DemoSeedVolume.Parse(volumeName);
    var timer = Stopwatch.StartNew();

    Console.WriteLine($"Starting Demo.DevMigrations '{action}' with volume '{volumeName}' and seed {randomSeed}...");

    var config = new ConfigurationBuilder()
      .SetBasePath(Directory.GetCurrentDirectory())
      .AddJsonFile("appsettings.json", optional: true)
      .AddJsonFile("demo/src/Demo.Api/appsettings.json", optional: true)
      .AddEnvironmentVariables()
      .Build();

    var postgres = config.GetConnectionString("DefaultConnection");

    DbContextOptions<AppDbContext> appOptions;
    DbContextOptions<OrgDbContext> orgOptions;
    DbContextOptions<EcomDbContext> ecomOptions;

    if (!string.IsNullOrEmpty(postgres))
    {
      appOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres).Options;
      orgOptions = new DbContextOptionsBuilder<OrgDbContext>().UseNpgsql(postgres).Options;
      ecomOptions = new DbContextOptionsBuilder<EcomDbContext>().UseNpgsql(postgres).Options;
    }
    else
    {
      var sqlite = "Data Source=demo.db";
      appOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(sqlite).Options;
      orgOptions = new DbContextOptionsBuilder<OrgDbContext>().UseSqlite(sqlite).Options;
      ecomOptions = new DbContextOptionsBuilder<EcomDbContext>().UseSqlite(sqlite).Options;
    }

    await using var appDb = new AppDbContext(appOptions);
    await using var orgDb = new OrgDbContext(orgOptions);
    await using var ecomDb = new EcomDbContext(ecomOptions);

    if (action == "reset")
    {
      Console.WriteLine("Deleting existing database...");
      await ecomDb.Database.EnsureDeletedAsync();
    }

    Console.WriteLine("Ensuring schema is created...");
    await ModuleSchema.EnsureCreatedAsync([appDb, orgDb, ecomDb]);

    var tenants = AppCodeRows.Tenants();

    Console.WriteLine("Seeding App (tenants, roles, users, groups via Bogus)...");
    var appTimer = Stopwatch.StartNew();
    await AppSeed.RunAsync(appDb, userGroupsCount: 20, usersCount: 60, randomSeed: randomSeed, tenants: tenants);
    appTimer.Stop();

    Console.WriteLine("Seeding Org (organizations, warehouses, counterparties, catalogue via Bogus)...");
    var orgTimer = Stopwatch.StartNew();
    await OrgSeed.RunAsync(orgDb, volume.Org, randomSeed: randomSeed, tenants: tenants);
    orgTimer.Stop();

    Console.WriteLine("Seeding Ecom (documents, lines, stock moves, links, balances via Bogus)...");
    var ecomTimer = Stopwatch.StartNew();
    var now = DateTimeOffset.UtcNow;
    await EcomSeed.RunAsync(ecomDb, volume.Ecom, randomSeed, tenants, now);
    ecomTimer.Stop();

    timer.Stop();

    var usersTotal = await appDb.AppUser.CountAsync();
    var groupsTotal = await appDb.AppUserGroup.CountAsync();
    var orgsTotal = await orgDb.OrgOrganization.CountAsync();
    var whsTotal = await orgDb.OrgWarehouse.CountAsync();
    var custsTotal = await orgDb.OrgCustomer.CountAsync();
    var prodsTotal = await orgDb.OrgProduct.CountAsync();
    var docsTotal = await ecomDb.EcomDoc.CountAsync();
    var ordersTotal = await ecomDb.EcomDoc.Where(d => d.DocTypeId == EcomData.DocType.CustomerOrderId).CountAsync();
    var linesTotal = await ecomDb.EcomDocLine.CountAsync();
    var movesTotal = await ecomDb.EcomStockMove.CountAsync();
    var balancesTotal = await ecomDb.EcomStockBalance.CountAsync();

    Console.WriteLine($"\nDemo database preparation completed in {timer.Elapsed.TotalSeconds:F2}s!");
    Console.WriteLine($"  - Tenants: {tenants.Length}");
    Console.WriteLine($"  - Users: {usersTotal}, Groups: {groupsTotal}");
    Console.WriteLine($"  - Organizations: {orgsTotal}, Warehouses: {whsTotal}");
    Console.WriteLine($"  - Customers: {custsTotal}, Products: {prodsTotal}");
    Console.WriteLine($"  - Documents: {docsTotal} (Customer Orders: {ordersTotal}), Lines: {linesTotal}");
    Console.WriteLine($"  - Stock Moves: {movesTotal}, Stock Balances: {balancesTotal}");

    return 0;
  }
}
