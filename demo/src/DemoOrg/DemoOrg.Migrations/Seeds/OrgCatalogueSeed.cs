namespace DemoOrg.Migrations.Seeds;

public static class OrgCatalogueSeed
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

    foreach (var tenant in tenants)
    {
      var tenantId = tenant.Row.Id;
      var faker = new Faker(tenant.Locale) { Random = random };

      // Brands
      var brandCount = Math.Max(2, volume.Brands);
      var existingBrands = await db.OrgBrand.Where(b => b.TenantId == tenantId).CountAsync(cancellationToken);
      if (existingBrands < brandCount)
      {
        var needed = brandCount - existingBrands;
        var brands = Enumerable.Range(existingBrands + 1, needed).Select(i => new OrgBrand
        {
          TenantId = tenantId,
          Name = $"{faker.Company.CompanyName()} Tech #{i}",
          Country = faker.Address.Country(),
          Website = $"https://www.{faker.Internet.DomainName()}",
          IsActive = true
        }).ToList();

        db.OrgBrand.AddRange(brands);
        await db.SaveChangesAsync(cancellationToken);
      }

      // Categories
      var catCount = Math.Max(2, volume.Categories);
      var existingCats = await db.OrgProductCategory.Where(c => c.TenantId == tenantId).CountAsync(cancellationToken);
      if (existingCats < catCount)
      {
        var needed = catCount - existingCats;
        var cats = Enumerable.Range(existingCats + 1, needed).Select(i => new OrgProductCategory
        {
          TenantId = tenantId,
          Name = $"{faker.Commerce.Department(1)} #{i}",
          ColorId = (i % 7) + 1,
          IsActive = true
        }).ToList();

        db.OrgProductCategory.AddRange(cats);
        await db.SaveChangesAsync(cancellationToken);
      }

      // DeliveryTypes & PaymentMethods
      if (!await db.OrgDeliveryType.AnyAsync(d => d.TenantId == tenantId, cancellationToken))
      {
        db.OrgDeliveryType.AddRange(
          new OrgDeliveryType { TenantId = tenantId, Name = "Express Courier", IsActive = true },
          new OrgDeliveryType { TenantId = tenantId, Name = "Standard Post Delivery", IsActive = true },
          new OrgDeliveryType { TenantId = tenantId, Name = "Self-Pickup from Warehouse", IsPickup = true, IsActive = true }
        );
        await db.SaveChangesAsync(cancellationToken);
      }

      if (!await db.OrgPaymentMethod.AnyAsync(p => p.TenantId == tenantId, cancellationToken))
      {
        db.OrgPaymentMethod.AddRange(
          new OrgPaymentMethod { TenantId = tenantId, Name = "Bank Wire Transfer (IBAN)", IsActive = true },
          new OrgPaymentMethod { TenantId = tenantId, Name = "Credit / Debit Card Online", IsActive = true },
          new OrgPaymentMethod { TenantId = tenantId, Name = "Cash on Delivery", IsCashOnDelivery = true, IsActive = true }
        );
        await db.SaveChangesAsync(cancellationToken);
      }

      // Products
      var brandIds = await db.OrgBrand.Where(b => b.TenantId == tenantId).Select(b => b.Id).ToListAsync(cancellationToken);
      var categoryIds = await db.OrgProductCategory.Where(c => c.TenantId == tenantId).Select(c => c.Id).ToListAsync(cancellationToken);
      var supplierIds = await db.OrgCounterparty.Where(c => c.TenantId == tenantId && c.IsSupplier).Select(c => c.Id).ToListAsync(cancellationToken);

      if (brandIds.Count > 0 && categoryIds.Count > 0 && supplierIds.Count > 0)
      {
        var productCount = Math.Max(5, volume.Products);
        var existingProducts = await db.OrgProduct.Where(p => p.TenantId == tenantId).CountAsync(cancellationToken);

        if (existingProducts < productCount)
        {
          var needed = productCount - existingProducts;
          var products = Enumerable.Range(existingProducts + 1, needed).Select(i =>
          {
            var purchasePrice = Math.Round(faker.Random.Decimal(100m, 50000m), 2);
            var markup = faker.Random.Decimal(1.15m, 1.5m);
            var price = Math.Round(purchasePrice * markup, 2);

            return new OrgProduct
            {
              TenantId = tenantId,
              SupplierId = faker.PickRandom(supplierIds),
              BrandId = faker.PickRandom(brandIds),
              CategoryId = faker.PickRandom(categoryIds),
              UnitId = 1,
              Article = $"ART-{tenantId}-{i:D5}",
              Name = $"{faker.Commerce.ProductAdjective()} {faker.Commerce.ProductName()} #{i}",
              Barcode = faker.Commerce.Ean13(),
              PurchasePrice = purchasePrice,
              Price = price,
              Weight = Math.Round(faker.Random.Decimal(0.1m, 25.0m), 2),
              IsActive = faker.Random.Bool(0.95f)
            };
          }).ToList();

          foreach (var batch in products.Chunk(BatchSize))
          {
            db.OrgProduct.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken);
          }
        }
      }
    }
  }
}
