using Bogus;
using DemoApp.Migrations.Seeds;
using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;
using DemoOrg.Data;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Migrations.Seeds;

public static class EcomExampleSeed
{
  private const int BatchSize = 500;
  private const int Days = EcomSeedClock.Days;
  private const int RecentDays = EcomSeedClock.RecentDays;
  private const int FreshDays = 2;
  private const int RecentEvery = 20;
  private const int MaxLines = 5;
  private const int MaxQty = 10;
  private const int SortStep = 10;

  private const string UkrainianLocale = "uk";
  private static readonly string[] PhoneCodes = ["44", "50", "63", "67", "93"];

  private static readonly long[] Open =
  [
    EcomData.DocStatus.CustomerOrderNewId, EcomData.DocStatus.CustomerOrderInProgressId, EcomData.DocStatus.CustomerOrderConfirmedId,
    EcomData.DocStatus.CustomerOrderPausedId
  ];

  private static readonly float[] FreshWeights = [0.5f, 0.3f, 0.15f, 0.05f];
  private static readonly float[] RecentWeights = [0.1f, 0.3f, 0.45f, 0.15f];

  private static readonly string[] UkrainianNotes =
  [
    "Зателефонувати перед доставкою.", "Доставити після 18:00.", "Потрібен рахунок на оплату.", "Подарункове пакування.",
    "Перевірити комплектність перед відправленням.", "Клієнт просив повідомити, коли замовлення буде готове."
  ];

  private static readonly string[] EnglishNotes =
  [
    "Call before the delivery.", "Deliver after 6 pm.", "An invoice for the payment is needed.", "Gift wrapping.",
    "Check the set is complete before shipping.", "The customer asked to be told when the order is ready."
  ];

  public static async Task RunAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int seed,
    IReadOnlyList<AppSeedTenant> tenants,
    DateTimeOffset now,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(volume);
    ArgumentNullException.ThrowIfNull(tenants);
    var random = new Randomizer(seed);
    foreach (var tenant in tenants)
    {
      var counters = new Dictionary<(long OrganizationId, int Year), int>();
      if (await OrdersAsync(db, volume, seed, random, tenant, tenants, now, counters, cancellationToken) is not { } orders)
      {
        continue;
      }

      foreach (var batch in orders.Chunk(BatchSize))
      {
        db.EcomDoc.AddRange(batch.Select(order => order.Doc));
        db.EcomDocLine.AddRange(batch.SelectMany(order => order.Lines));
        await db.SaveChangesAsync(true, cancellationToken);
        db.ChangeTracker.Clear();
      }

      db.EcomDocCounter.AddRange(counters.OrderBy(counter => counter.Key.OrganizationId).ThenBy(counter => counter.Key.Year)
        .Select(counter => new EcomDocCounter
        {
          TenantId = tenant.Row.Id,
          OrganizationId = counter.Key.OrganizationId,
          DocTypeId = EcomData.DocType.CustomerOrderId,
          Year = counter.Key.Year,
          LastNumber = counter.Value
        }));
      await db.SaveChangesAsync(true, cancellationToken);
      db.ChangeTracker.Clear();
    }
  }

  internal static async Task<IEnumerable<(EcomDoc Doc, EcomDocLine[] Lines)>?> OrdersAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int seed,
    Randomizer random,
    AppSeedTenant tenant,
    IReadOnlyList<AppSeedTenant> tenants,
    DateTimeOffset now,
    Dictionary<(long OrganizationId, int Year), int> counters,
    CancellationToken cancellationToken
  )
  {
    if (await ScopeAsync(db, tenant, tenants, random, volume.OpeningLines, seed, cancellationToken) is not { } scope)
    {
      return null;
    }

    var prefix = EcomCodeRows.DocTypes().Single(type => type.Id == EcomData.DocType.CustomerOrderId).Prefix;
    var posted = EcomCodeRows.DocStatuses().Where(status => status.IsPosted).Select(status => status.Id).ToHashSet();
    return Orders(scope, volume.CustomerOrders, prefix, posted, EcomSeedClock.Today(scope.Zone, now), counters);
  }

  private static async Task<Scope?> ScopeAsync(
    EcomDbContext db,
    AppSeedTenant tenant,
    IReadOnlyList<AppSeedTenant> tenants,
    Randomizer random,
    int openingLines,
    int seed,
    CancellationToken cancellationToken
  )
  {
    var tenantId = tenant.Row.Id;
    var products = await db.OrgProduct.IgnoreQueryFilters()
      .Where(product => product.TenantId == tenantId && product.IsActive && product.Supplier.IsSupplier && product.Supplier.IsActive)
      .Select(product => new Product(
        product.Id,
        product.Price,
        product.UnitId != OrgData.Unit.PieceId && product.UnitId != OrgData.Unit.SetId
      ))
      .ToDictionaryAsync(product => product.Id, cancellationToken);

    var assortments = (await EcomSeedAssortment.OfAsync(db, tenantId, openingLines, seed, cancellationToken))
      .Where(of => of.Value.Length > 0).ToDictionary(of => of.Key, of => of.Value.Select(product => products[product]).ToArray());

    var warehouses = await db.OrgWarehouse.IgnoreQueryFilters().Where(warehouse => warehouse.TenantId == tenantId && warehouse.IsActive)
      .OrderBy(warehouse => warehouse.Id).Select(warehouse => new { warehouse.Id, warehouse.OrganizationId })
      .ToListAsync(cancellationToken);
    var organizations = (await db.OrgOrganization.IgnoreQueryFilters()
        .Where(organization => organization.TenantId == tenantId && organization.IsActive).OrderBy(organization => organization.Id)
        .Select(organization => new { organization.Id, organization.Code, organization.CurrencyId }).ToListAsync(cancellationToken))
      .Select(organization => new Organization(
        organization.Id,
        organization.Code,
        organization.CurrencyId,
        [
          .. warehouses.Where(warehouse => warehouse.OrganizationId == organization.Id && assortments.ContainsKey(warehouse.Id))
            .Select(warehouse => warehouse.Id)
        ]
      )).Where(organization => organization.Warehouses.Length > 0).ToArray();
    var customers = await db.OrgCustomer.IgnoreQueryFilters().Where(customer => customer.TenantId == tenantId)
      .OrderBy(customer => customer.Id).Select(customer => new Customer(
        customer.Id,
        customer.Name,
        customer.Phone,
        customer.City,
        customer.DiscountPercent,
        customer.ManagerId,
        customer.DeliveryTypeId,
        customer.PaymentMethodId,
        customer.PaymentTermId
      )).ToArrayAsync(cancellationToken);
    if (organizations.Length == 0 || customers.Length == 0)
    {
      return null;
    }

    var contacts = new Dictionary<long, Contact>();
    foreach (var contact in await db.OrgCustomerContact.IgnoreQueryFilters()
               .Where(contact => contact.TenantId == tenantId && contact.IsPrimary).OrderBy(contact => contact.Id)
               .Select(contact => new { contact.CustomerId, Row = new Contact(contact.Id, contact.Name, contact.Phone) })
               .ToListAsync(cancellationToken))
    {
      contacts.TryAdd(contact.CustomerId, contact.Row);
    }

    var deliveryTypes = await db.OrgDeliveryType.IgnoreQueryFilters().Where(type => type.TenantId == tenantId).OrderBy(type => type.Id)
      .Select(type => new { type.Id, type.IsPickup }).ToListAsync(cancellationToken);
    return new Scope(
      tenantId,
      new Faker(tenant.Locale) { Random = random },
      EcomSeedClock.ZoneOf(tenant, tenants),
      tenant.Locale == UkrainianLocale ? UkrainianNotes : EnglishNotes,
      organizations,
      customers,
      contacts,
      assortments,
      await db.AppUser.IgnoreQueryFilters().Where(user => user.TenantId == tenantId).OrderBy(user => user.Id).Select(user => user.Id)
        .ToArrayAsync(cancellationToken),
      [.. deliveryTypes.Select(type => type.Id)],
      [.. deliveryTypes.Where(type => type.IsPickup).Select(type => type.Id)],
      await db.OrgPaymentMethod.IgnoreQueryFilters().Where(method => method.TenantId == tenantId).OrderBy(method => method.Id)
        .Select(method => method.Id).ToArrayAsync(cancellationToken),
      await db.OrgPaymentTerm.OrderBy(term => term.Id).Select(term => term.Id).ToArrayAsync(cancellationToken)
    );
  }

  private static IEnumerable<(EcomDoc Doc, EcomDocLine[] Lines)> Orders(
    Scope scope,
    int count,
    string prefix,
    HashSet<long> posted,
    DateOnly today,
    Dictionary<(long OrganizationId, int Year), int> counters
  )
  {
    var faker = scope.Faker;
    var random = faker.Random;
    var ages = Enumerable.Range(0, count).Select(index => random.Int(1, index % RecentEvery == 0 ? RecentDays : Days)).OrderDescending()
      .ToArray();
    foreach (var age in ages)
    {
      var date = today.AddDays(-age);
      var organization = random.ArrayElement(scope.Organizations);
      var customer = random.ArrayElement(scope.Customers);
      var statusId = StatusOf(random, age);
      var number = counters[(organization.Id, date.Year)] = counters.GetValueOrDefault((organization.Id, date.Year)) + 1;
      var deliveryTypeId = customer.DeliveryTypeId ?? OneOf(random, scope.DeliveryTypes);
      var contact = scope.Contacts.GetValueOrDefault(customer.Id) is { } primary && random.Bool(0.3f) ? primary : null;
      var named = random.Bool(0.85f);
      var doc = new EcomDoc
      {
        TenantId = scope.TenantId,
        OrganizationId = organization.Id,
        DocTypeId = EcomData.DocType.CustomerOrderId,
        DocStatusId = statusId,
        Number = EcomDocNumbers.Format(prefix, organization.Code, date.Year, number),
        Date = date,
        CustomerId = customer.Id,
        ContactId = contact?.Id,
        WarehouseId = random.ArrayElement(organization.Warehouses),
        DeliveryTypeId = deliveryTypeId,
        PaymentMethodId = customer.PaymentMethodId ?? OneOf(random, scope.PaymentMethods),
        PaymentTermId = customer.PaymentTermId ?? OneOf(random, scope.PaymentTerms),
        ManagerId = customer.ManagerId ?? OneOf(random, scope.Managers),
        CurrencyId = organization.CurrencyId,
        RecipientName = named ? contact?.Name ?? customer.Name : null,
        RecipientPhone = named ? contact?.Phone ?? customer.Phone ?? NewPhone(faker) : null,
        DeliveryAddress = named && !(deliveryTypeId is { } type && scope.Pickups.Contains(type)) ? Address(faker, customer) : null,
        Note = random.Bool(0.2f) ? faker.PickRandom(scope.Notes) : null,
        PostedAt = posted.Contains(statusId) ? EcomSeedClock.At(scope.Zone, date, random) : null
      };
      var lines = Lines(scope, doc, customer.DiscountPercent);
      doc.Qty = lines.Sum(line => line.Qty);
      doc.Amount = lines.Sum(line => line.Amount);
      yield return (doc, lines);
    }
  }

  private static EcomDocLine[] Lines(Scope scope, EcomDoc doc, decimal discountPercent)
  {
    var random = scope.Faker.Random;
    var assortment = scope.Assortments[doc.WarehouseId!.Value];
    var count = Math.Min(random.Int(1, MaxLines), assortment.Length);
    var products = new List<Product>(count);
    while (products.Count < count)
    {
      var product = random.ArrayElement(assortment);
      if (!products.Contains(product))
      {
        products.Add(product);
      }
    }

    return
    [
      .. products.Select((product, index) =>
      {
        var qty = product.Measured && random.Bool() ? random.Int(1, MaxQty - 1) + 0.5m : random.Int(1, MaxQty);
        return new EcomDocLine
        {
          TenantId = scope.TenantId,
          Doc = doc,
          SortOrder = (index + 1) * SortStep,
          ProductId = product.Id,
          Qty = qty,
          Price = product.Price,
          DiscountPercent = discountPercent,
          Amount = EcomDocTotals.LineAmount(qty, product.Price, discountPercent)
        };
      })
    ];
  }

  private static long StatusOf(Randomizer random, int age) => age switch
  {
    <= FreshDays => random.WeightedRandom(Open, FreshWeights),
    <= RecentDays => random.WeightedRandom(Open, RecentWeights),
    _ => random.Bool(0.9f) ? EcomData.DocStatus.CustomerOrderCompletedId : EcomData.DocStatus.CustomerOrderCanceledId
  };

  private static string Address(Faker faker, Customer customer)
  {
    var street = faker.Address.StreetAddress();
    return customer.City is { Length: > 0 } city ? $"{city}, {street}" : street;
  }

  private static string NewPhone(Faker faker) => "+380" + faker.PickRandom(PhoneCodes) + faker.Random.ReplaceNumbers("#######");

  private static long? OneOf(Randomizer random, long[] keys) => keys.Length > 0 ? random.ArrayElement(keys) : null;

  private sealed record Scope(
    long TenantId,
    Faker Faker,
    TimeZoneInfo Zone,
    string[] Notes,
    Organization[] Organizations,
    Customer[] Customers,
    Dictionary<long, Contact> Contacts,
    Dictionary<long, Product[]> Assortments,
    long[] Managers,
    long[] DeliveryTypes,
    HashSet<long> Pickups,
    long[] PaymentMethods,
    long[] PaymentTerms
  );

  private sealed record Organization(long Id, string Code, long CurrencyId, long[] Warehouses);

  private sealed record Customer(
    long Id,
    string Name,
    string? Phone,
    string? City,
    decimal DiscountPercent,
    long? ManagerId,
    long? DeliveryTypeId,
    long? PaymentMethodId,
    long? PaymentTermId
  );

  private sealed record Contact(long Id, string Name, string? Phone);

  private sealed record Product(long Id, decimal Price, bool Measured);
}
