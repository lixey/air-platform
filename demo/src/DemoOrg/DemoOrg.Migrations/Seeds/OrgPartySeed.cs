using Gender = Bogus.DataSets.Name.Gender;

namespace DemoOrg.Migrations.Seeds;

public static class OrgPartySeed
{
  private const int BatchSize = 500;

  public static async Task RunAsync(
    OrgDbContext db,
    OrgSeedVolume volume,
    int randomSeed,
    IReadOnlyList<AppSeedTenant> tenants,
    CancellationToken cancellationToken = default
  )
  {
    var random = new Randomizer(randomSeed);
    var fakers = tenants.Select(t => t.Locale).Distinct()
      .ToDictionary(loc => loc, loc => new Faker(loc) { Random = random });

    foreach (var tenant in tenants)
    {
      var tenantId = tenant.Row.Id;
      var faker = fakers[tenant.Locale];

      // Counterparties
      var counterpartyCount = Math.Max(2, volume.Suppliers + volume.Buyers);
      var existingCps = await db.OrgCounterparty.Where(c => c.TenantId == tenantId).CountAsync(cancellationToken);

      if (existingCps < counterpartyCount)
      {
        var needed = counterpartyCount - existingCps;
        var cps = Enumerable.Range(existingCps + 1, needed).Select(i =>
        {
          var isSupplier = i <= volume.Suppliers;
          return new OrgCounterparty
          {
            TenantId = tenantId,
            Name = $"{faker.Company.CompanyName()} {(isSupplier ? "Supplies" : "Trade")} #{i}",
            TaxId = $"{faker.Finance.Account(5)}{i:D3}",
            IsSupplier = isSupplier,
            IsBuyer = !isSupplier || i % 4 == 0,
            IsActive = faker.Random.Bool(0.95f)
          };
        }).ToList();

        foreach (var batch in cps.Chunk(BatchSize))
        {
          db.OrgCounterparty.AddRange(batch);
          await db.SaveChangesAsync(cancellationToken);
        }
      }

      // Customers
      var customerCount = Math.Max(2, volume.Customers);
      var existingCustomers = await db.OrgCustomer.Where(c => c.TenantId == tenantId).CountAsync(cancellationToken);

      if (existingCustomers < customerCount)
      {
        var needed = customerCount - existingCustomers;
        var customers = Enumerable.Range(existingCustomers + 1, needed).Select(i => new OrgCustomer
        {
          TenantId = tenantId,
          Name = i % 2 == 0 ? $"{faker.Company.CompanyName()} #{i}" : $"{faker.Name.FullName()} (Entrepreneur #{i})",
          Email = faker.Internet.Email(),
          Phone = faker.Phone.PhoneNumber(),
          City = faker.Address.City(),
          IsActive = faker.Random.Bool(0.95f)
        }).ToList();

        foreach (var batch in customers.Chunk(BatchSize))
        {
          db.OrgCustomer.AddRange(batch);
          await db.SaveChangesAsync(cancellationToken);
        }
      }

      // Customer contacts
      var customerIds = await db.OrgCustomer.Where(c => c.TenantId == tenantId).Select(c => c.Id).Take(100).ToListAsync(cancellationToken);
      var existingContacts = await db.OrgCustomerContact.Where(c => c.TenantId == tenantId).CountAsync(cancellationToken);
      if (existingContacts < customerIds.Count)
      {
        var contacts = customerIds.Select(cid =>
        {
          var gender = faker.PickRandom<Gender>();
          return new OrgCustomerContact
          {
            TenantId = tenantId,
            CustomerId = cid,
            Name = $"{faker.Name.FirstName(gender)} {faker.Name.LastName(gender)}",
            Phone = faker.Phone.PhoneNumber(),
            Email = faker.Internet.Email()
          };
        }).ToList();

        db.OrgCustomerContact.AddRange(contacts);
        await db.SaveChangesAsync(cancellationToken);
      }
    }
  }
}
