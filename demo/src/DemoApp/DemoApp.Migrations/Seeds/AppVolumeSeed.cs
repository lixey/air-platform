using Gender = Bogus.DataSets.Name.Gender;

namespace DemoApp.Migrations.Seeds;

public static class AppVolumeSeed
{
  private const int BatchSize = 500;

  public static async Task RunAsync(
    AppDbContext db,
    int userGroupsCount,
    int usersCount,
    int randomSeed,
    IReadOnlyList<AppSeedTenant> tenants,
    CancellationToken cancellationToken = default,
    string? seedPassword = null
  )
  {
    var passwordHash = new PasswordHasher().HashPassword(AppIdentitySeed.ResolveSeedPassword(seedPassword));
    var random = new Randomizer(randomSeed);
    var fakers = tenants.Select(t => t.Locale).Distinct()
      .ToDictionary(loc => loc, loc => new Faker(loc) { Random = random });

    var colors = await db.AppColor.Select(c => c.Id).ToListAsync(cancellationToken);

    for (var t = 0; t < tenants.Count; t++)
    {
      var tenant = tenants[t].Row;
      var faker = fakers[tenants[t].Locale];
      var groupsForTenant = Math.Max(1, userGroupsCount / tenants.Count);

      var existingGroups = await db.AppUserGroup.Where(g => g.TenantId == tenant.Id).CountAsync(cancellationToken);
      if (existingGroups < groupsForTenant)
      {
        var groups = Enumerable.Range(existingGroups + 1, groupsForTenant - existingGroups).Select(i => new AppUserGroup
        {
          TenantId = tenant.Id,
          Name = $"{faker.Commerce.Department(1)} {i:D3}",
          Description = faker.Random.Bool(0.7f) ? faker.Company.CatchPhrase() : null,
          ColorId = colors.Count > 0 ? faker.PickRandom(colors) : 1,
          IsActive = faker.Random.Bool(0.9f)
        }).ToList();

        db.AppUserGroup.AddRange(groups);
        await db.SaveChangesAsync(cancellationToken);
      }

      var groupIds = await db.AppUserGroup.Where(g => g.TenantId == tenant.Id).Select(g => g.Id).ToListAsync(cancellationToken);
      var usersForTenant = Math.Max(1, usersCount / tenants.Count);
      var existingUsers = await db.AppUser.Where(u => u.TenantId == tenant.Id).CountAsync(cancellationToken);

      if (existingUsers < usersForTenant)
      {
        var needed = usersForTenant - existingUsers;
        var users = Enumerable.Range(existingUsers + 1, needed).Select(i =>
        {
          var gender = faker.PickRandom<Gender>();
          var login = $"user{tenant.Id}_{i:D4}";
          return new AppUser
          {
            TenantId = tenant.Id,
            Login = login,
            Name = $"{faker.Name.FirstName(gender)} {faker.Name.LastName(gender)}",
            Email = $"{login}@{tenant.Handle.ToLowerInvariant()}.demo",
            PasswordHash = passwordHash,
            UserGroupId = groupIds.Count > 0 && faker.Random.Bool(0.85f) ? faker.PickRandom(groupIds) : null,
            IsActive = faker.Random.Bool(0.95f)
          };
        }).ToList();

        foreach (var batch in users.Chunk(BatchSize))
        {
          db.AppUser.AddRange(batch);
          await db.SaveChangesAsync(cancellationToken);
        }
      }

      var usersWithoutPassword = await db.AppUser
        .Where(u => u.TenantId == tenant.Id && (u.PasswordHash == null || u.PasswordHash == ""))
        .ToListAsync(cancellationToken);
      if (usersWithoutPassword.Count > 0)
      {
        foreach (var u in usersWithoutPassword)
        {
          u.PasswordHash = passwordHash;
        }

        await db.SaveChangesAsync(cancellationToken);
      }
    }
  }
}
