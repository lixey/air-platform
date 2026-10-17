using Bogus;
using DemoApp.Migrations.Seeds;
using DemoEcom.Data;
using DemoEcom.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Migrations.Seeds;

public static class EcomSalesSeed
{
  private const int BatchSize = 250;

  public static async Task RunAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
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

      var orgIds = await db.OrgOrganization.Where(o => o.TenantId == tenantId).Select(o => o.Id).ToListAsync(cancellationToken);
      var whIds = await db.OrgWarehouse.Where(w => w.TenantId == tenantId).Select(w => w.Id).ToListAsync(cancellationToken);
      var custIds = await db.OrgCustomer.Where(c => c.TenantId == tenantId).Select(c => c.Id).ToListAsync(cancellationToken);
      var userIds = await db.AppUser.Where(u => u.TenantId == tenantId).Select(u => u.Id).ToListAsync(cancellationToken);
      var products = await db.OrgProduct.Where(p => p.TenantId == tenantId).Select(p => new { p.Id, p.Price }).ToListAsync(cancellationToken);
      var deliveryTypeIds = await db.OrgDeliveryType.Where(d => d.TenantId == tenantId).Select(d => d.Id).ToListAsync(cancellationToken);
      var paymentMethodIds = await db.OrgPaymentMethod.Where(p => p.TenantId == tenantId).Select(p => p.Id).ToListAsync(cancellationToken);
      var paymentTermIds = await db.OrgPaymentTerm.Select(p => p.Id).ToListAsync(cancellationToken);

      if (orgIds.Count == 0 || whIds.Count == 0 || custIds.Count == 0 || products.Count == 0) continue;

      var orderCount = Math.Max(5, volume.CustomerOrders);
      var existingOrders = await db.EcomDoc.Where(d => d.TenantId == tenantId && d.DocTypeId == EcomData.DocType.CustomerOrderId).CountAsync(cancellationToken);

      if (existingOrders < orderCount)
      {
        var needed = orderCount - existingOrders;
        var statuses = new[]
        {
          EcomData.DocStatus.CustomerOrderNewId,
          EcomData.DocStatus.CustomerOrderInProgressId,
          EcomData.DocStatus.CustomerOrderConfirmedId,
          EcomData.DocStatus.CustomerOrderCompletedId
        };

        var orders = new List<EcomDoc>();
        var orderLines = new List<EcomDocLine>();

        for (var i = existingOrders + 1; i <= orderCount; i++)
        {
          var linesCount = faker.Random.Int(1, 4);
          var selectedProducts = faker.PickRandom(products, linesCount).ToList();

          var totalQty = 0;
          var totalAmount = 0m;

          var doc = new EcomDoc
          {
            TenantId = tenantId,
            OrganizationId = faker.PickRandom(orgIds),
            CurrencyId = 1,
            DocTypeId = EcomData.DocType.CustomerOrderId,
            DocStatusId = faker.PickRandom(statuses),
            Number = $"ORD-2026-{tenantId}-{i:D4}",
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-faker.Random.Int(0, 30))),
            CustomerId = faker.PickRandom(custIds),
            WarehouseId = faker.PickRandom(whIds),
            ManagerId = userIds.Count > 0 ? faker.PickRandom(userIds) : null,
            DeliveryTypeId = deliveryTypeIds.Count > 0 ? faker.PickRandom(deliveryTypeIds) : null,
            PaymentMethodId = paymentMethodIds.Count > 0 ? faker.PickRandom(paymentMethodIds) : null,
            PaymentTermId = paymentTermIds.Count > 0 ? faker.PickRandom(paymentTermIds) : null,
            DeliveryAddress = faker.Address.FullAddress(),
            Note = faker.Random.Bool(0.6f) ? faker.Lorem.Sentence() : null
          };

          for (var l = 0; l < selectedProducts.Count; l++)
          {
            var p = selectedProducts[l];
            var qty = faker.Random.Int(1, 10);
            var lineAmount = qty * p.Price;

            totalQty += qty;
            totalAmount += lineAmount;

            orderLines.Add(new EcomDocLine
            {
              Doc = doc,
              TenantId = tenantId,
              ProductId = p.Id,
              SortOrder = l + 1,
              Qty = qty,
              QtyReserved = qty,
              QtyDone = 0,
              Price = p.Price,
              Amount = lineAmount
            });
          }

          doc.Qty = totalQty;
          doc.Amount = totalAmount;
          doc.PaidAmount = faker.Random.Bool(0.5f) ? totalAmount : (faker.Random.Bool(0.3f) ? Math.Round(totalAmount * 0.5m, 2) : 0m);

          orders.Add(doc);
        }

        foreach (var batch in orders.Chunk(BatchSize))
        {
          db.EcomDoc.AddRange(batch);
          await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var batch in orderLines.Chunk(BatchSize * 2))
        {
          db.EcomDocLine.AddRange(batch);
          await db.SaveChangesAsync(cancellationToken);
        }
      }
    }
  }
}
