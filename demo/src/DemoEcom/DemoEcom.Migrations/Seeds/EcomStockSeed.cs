using Air.Data.Tenancy;
using Bogus;
using DemoApp.Migrations.Seeds;
using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Migrations.Seeds;

/// <summary>
/// The stock documents of Ecom's examples, the same volume for each tenant, as steps of the walk of the examples
/// (<see cref="EcomSeedWalk"/>). Every active warehouse of an active organisation trades in an assortment, the volume's
/// opening lines of the active products of the tenant's active suppliers (<see cref="EcomSeedAssortment"/>), and opens
/// with a posted stock receipt of that assortment on the first of the thirty days before the tenant's reference day
/// (<see cref="EcomSeedClock.Today"/>): each line brings what the completed customer orders of the warehouse ask of its
/// product and a surplus, small for some products, so the orders of the last week find some goods short. The other
/// documents, purchase orders, supplier invoices, stock issues and stock transfers, are dated over those thirty days,
/// the newest the day before the reference day, a step each. A purchase order and a
/// supplier invoice name a warehouse, a supplier and the supplier's products of the warehouse's assortment; a stock issue
/// and a stock transfer, which goes to another warehouse of the same organisation, name only products their warehouse
/// holds on their date and take no more than is free there less the demand still to ship (<see cref="EcomSeedBooks.Free"/>),
/// so no balance goes below zero on any date and every completed order ships in full. Every line is priced at its
/// product's purchase price. One purchase order in ten is of the last week, Sent or New in turn; an older one is
/// Completed or, every eighth, Canceled. Of ten supplier invoices, stock issues or stock transfers seven are Posted, two
/// Draft and one Canceled, in turn. The notes are in the tenant's language, taken by its locale. A tenant without an
/// organisation that has a warehouse or without an assortment gets no stock document; an organisation with one warehouse
/// gets no transfer, and a document that finds nothing to name is not written. The random generator is the stock
/// documents' own, so the customer orders and the sales draw nothing from it. No link and no file is written.
/// </summary>
internal static class EcomStockSeed
{
  /// <summary>The purchase order that is dated in the last week: every tenth; and the older one that is canceled: every eighth.</summary>
  private const int RecentEvery = 10;

  private const int MaxLines = 5;
  private const int SortStep = 10;

  /// <summary>
  /// What a line of the opening receipt brings over the demand of the completed orders: little for the share of the
  /// products that are scarce, plenty for the others.
  /// </summary>
  private const float ScarceShare = 0.3f;

  private const int MaxScarceQty = 5;
  private const int MinOpeningQty = 20;
  private const int MaxOpeningQty = 200;

  /// <summary>What a line of a purchase asks, and the most a line takes or moves.</summary>
  private const int MinPurchaseQty = 5;
  private const int MaxPurchaseQty = 50;
  private const int MaxIssueQty = 10;
  private const int MaxTransferQty = 20;

  /// <summary>The locale of a tenant that reads Ukrainian; every other locale reads English.</summary>
  private const string UkrainianLocale = "uk";

  private static readonly EcomStockTexts Ukrainian = new(
    "Початкові залишки.",
    [
      "Постачання частинами за погодженням.", "Потрібні сертифікати якості.", "Доставити на склад до 12:00.",
      "Ціни узгоджено з менеджером постачальника.", "Оплата після перевірки кількості."
    ],
    [
      "Пошкоджено під час зберігання.", "Нестача за результатами інвентаризації.", "Зразки для виставки.",
      "Брак, виявлений під час перевірки.", "Списано на власні потреби."
    ],
    ["Поповнення залишків складу.", "Переміщення під замовлення клієнта.", "Вирівнювання залишків між складами."]
  );

  private static readonly EcomStockTexts English = new(
    "Opening stock.",
    [
      "Delivery in parts as agreed.", "Quality certificates are needed.", "Deliver to the warehouse before noon.",
      "The prices are agreed with the supplier's manager.", "Payment after the quantity is checked."
    ],
    [
      "Damaged in storage.", "A shortage found by the stocktaking.", "Samples for the exhibition.", "Defects found at the check.",
      "Written off for the company's own needs."
    ],
    ["Restocking the warehouse.", "Moved for a customer's order.", "Evening out the stock between the warehouses."]
  );

  /// <summary>
  /// The steps of the stock documents of <paramref name="tenant"/>, in the order they are planned: the opening receipt of
  /// each warehouse on the first of the thirty days, then the purchase orders, the supplier invoices, the stock issues
  /// and the stock transfers, each on its date, so on one date the purchases stand before what takes the stock. None
  /// when the tenant has nothing a stock document names. The dates are counted back from the tenant's own day of
  /// <paramref name="now"/>, by the clock of <paramref name="books"/>; they are drawn here and a document when the walk
  /// reaches its step, both from <paramref name="random"/>, the one generator of the stock documents of every tenant; a step leaves
  /// its document through <paramref name="books"/>.
  /// </summary>
  public static async Task<IReadOnlyList<EcomSeedStep>> StepsAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int seed,
    Randomizer random,
    AppSeedTenant tenant,
    EcomSeedBooks books,
    DateTimeOffset now,
    CancellationToken cancellationToken
  )
  {
    if (await ScopeAsync(db, tenant, random, volume.OpeningLines, seed, cancellationToken) is not { } scope)
    {
      return [];
    }

    var today = EcomSeedClock.Today(books.Zone, now);
    var opening = today.AddDays(-EcomSeedClock.Days);
    // The dates first, so the walk numbers and posts the documents from the oldest to the newest; an age starts at one,
    // so no document is dated the reference day
    (long TypeId, int Age)[] plans =
    [
      .. Enumerable.Range(0, volume.PurchaseOrders).Select(index => (
        EcomData.DocType.PurchaseOrderId,
        index % RecentEvery == 0 ? random.Int(1, EcomSeedClock.RecentDays) : random.Int(EcomSeedClock.RecentDays + 1, EcomSeedClock.Days)
      )),
      .. Enumerable.Range(0, volume.SupplierInvoices).Select(_ => (EcomData.DocType.SupplierInvoiceId, random.Int(1, EcomSeedClock.Days))),
      .. Enumerable.Range(0, volume.StockIssues).Select(_ => (EcomData.DocType.StockIssueId, random.Int(1, EcomSeedClock.Days))),
      .. Enumerable.Range(0, volume.StockTransfers).Select(_ => (EcomData.DocType.StockTransferId, random.Int(1, EcomSeedClock.Days)))
    ];
    var turns = new EcomStockTurns();
    return
    [
      .. scope.Shelves.Select(shelf => new EcomSeedStep(opening, EcomSeedStep.Stock, () => [Opening(scope, shelf, opening, books)])),
      .. plans.Select(plan => new EcomSeedStep(
        today.AddDays(-plan.Age),
        EcomSeedStep.Stock,
        () => Written(scope, books, turns, plan.TypeId, plan.Age, today.AddDays(-plan.Age))
      ))
    ];
  }

  /// <summary>
  /// What the tenant's stock documents name, as the database keeps it, each list in the order of its keys; nothing when
  /// the tenant has no active organisation with an active warehouse or its warehouses get no assortment.
  /// </summary>
  private static async Task<EcomStockScope?> ScopeAsync(
    EcomDbContext db,
    AppSeedTenant tenant,
    Randomizer random,
    int openingLines,
    int seed,
    CancellationToken cancellationToken
  )
  {
    var tenantId = tenant.Row.Id;
    // A document names rows the database gave a key to, never a position in the sequence
    var warehouses = await db.OrgWarehouse.IgnoreTenantFilter()
      .Where(warehouse => warehouse.TenantId == tenantId && warehouse.IsActive && warehouse.Organization.IsActive)
      .OrderBy(warehouse => warehouse.Id)
      .Select(warehouse => new { warehouse.Id, warehouse.OrganizationId, warehouse.Organization.CurrencyId })
      .ToListAsync(cancellationToken);
    // Only an active supplier is named, as the screens offer no other, and only its active products
    var suppliers = await db.OrgCounterparty.IgnoreTenantFilter()
      .Where(supplier => supplier.TenantId == tenantId && supplier.IsSupplier && supplier.IsActive).OrderBy(supplier => supplier.Id)
      .Select(supplier => new EcomStockSupplier(supplier.Id, supplier.PaymentDelayDays, supplier.DeliveryDays))
      .ToDictionaryAsync(supplier => supplier.Id, cancellationToken);
    var products = await db.OrgProduct.IgnoreTenantFilter()
      .Where(product => product.TenantId == tenantId && product.IsActive && product.Supplier.IsSupplier && product.Supplier.IsActive)
      .OrderBy(product => product.Id).Select(product => new EcomStockProduct(product.Id, product.SupplierId, product.PurchasePrice))
      .ToDictionaryAsync(product => product.Id, cancellationToken);
    var assortment = await EcomSeedAssortment.OfAsync(db, tenantId, openingLines, seed, cancellationToken);
    var shelves = warehouses.Select(warehouse =>
    {
      var own = assortment[warehouse.Id].Select(product => products[product]).ToArray();
      return new EcomStockShelf(
        warehouse.Id,
        warehouse.OrganizationId,
        warehouse.CurrencyId,
        own,
        [.. own.GroupBy(product => product.SupplierId).Select(of => new EcomStockOffer(suppliers[of.Key], [.. of]))]
      );
    }).Where(shelf => shelf.Assortment.Length > 0).ToArray();
    if (shelves.Length == 0)
    {
      return null;
    }

    return new EcomStockScope(
      tenantId,
      new Faker(tenant.Locale) { Random = random },
      tenant.Locale == UkrainianLocale ? Ukrainian : English,
      shelves,
      [.. shelves.GroupBy(shelf => shelf.OrganizationId).Where(of => of.Count() > 1).Select(of => of.ToArray())],
      products,
      await db.AppUser.IgnoreTenantFilter().Where(user => user.TenantId == tenantId).OrderBy(user => user.Id).Select(user => user.Id)
        .ToArrayAsync(cancellationToken),
      await db.OrgPaymentTerm.OrderBy(term => term.Id).Select(term => term.Id).ToArrayAsync(cancellationToken)
    );
  }

  /// <summary>
  /// The document of <paramref name="typeId"/> dated <paramref name="date"/>, <paramref name="age"/> days before the
  /// reference day, as the walk leaves it: drafted over what the warehouses hold then, in the status its turn gives,
  /// numbered and posted through <paramref name="books"/>. None when the draft finds nothing to name: it takes no number
  /// and no turn of a status.
  /// </summary>
  private static IReadOnlyList<EcomSeedWritten> Written(EcomStockScope scope, EcomSeedBooks books, EcomStockTurns turns, long typeId, int age, DateOnly date)
  {
    var draft = typeId switch
    {
      EcomData.DocType.PurchaseOrderId or EcomData.DocType.SupplierInvoiceId => Purchase(scope, typeId, date),
      EcomData.DocType.StockIssueId => Issue(scope, date, books),
      _ => Transfer(scope, date, books)
    };
    if (draft is not var (doc, lines))
    {
      return [];
    }

    return [books.Finish(doc, lines, turns.Next(typeId, age), scope.Faker.Random)];
  }

  /// <summary>
  /// The stock receipt <paramref name="shelf"/> opens with, posted through <paramref name="books"/>: a line of every
  /// product of its assortment, sized to the demand, what the completed customer orders of the warehouse ask of the
  /// product, all of it still to ship on the first date, in whole units, and a surplus of at least one.
  /// </summary>
  private static EcomSeedWritten Opening(EcomStockScope scope, EcomStockShelf shelf, DateOnly date, EcomSeedBooks books)
  {
    var random = scope.Faker.Random;
    var doc = NewDoc(scope, EcomData.DocType.StockReceiptId, shelf, date);
    doc.Note = scope.Texts.OpeningNote;
    var lines = LinesOf(scope, doc, shelf.Assortment.Select(product =>
    {
      var demand = decimal.Ceiling(books.ToShip(new EcomStockKey(shelf.OrganizationId, shelf.WarehouseId, product.Id)));
      var surplus = random.Bool(ScarceShare) ? random.Int(1, MaxScarceQty) : random.Int(MinOpeningQty, MaxOpeningQty);
      return (product, demand + surplus);
    }));
    return books.Finish(doc, lines, EcomData.DocStatus.StockReceiptPostedId, random);
  }

  /// <summary>
  /// A purchase order or a supplier invoice of <paramref name="typeId"/>: a warehouse, a supplier whose products the
  /// warehouse trades in, and one to <see cref="MaxLines"/> of those products. The order is due when the supplier
  /// delivers, the invoice when the supplier is to be paid, and the invoice carries the supplier's own number.
  /// </summary>
  private static (EcomDoc Doc, EcomDocLine[] Lines)? Purchase(EcomStockScope scope, long typeId, DateOnly date)
  {
    var random = scope.Faker.Random;
    var shelf = random.ArrayElement(scope.Shelves);
    var offer = random.ArrayElement(shelf.Offers);
    var doc = NewDoc(scope, typeId, shelf, date);
    doc.CounterpartyId = offer.Supplier.Id;
    if (typeId == EcomData.DocType.PurchaseOrderId)
    {
      doc.PaymentTermId = OneOf(random, scope.PaymentTerms);
      doc.DueDate = date.AddDays(offer.Supplier.DeliveryDays);
    }
    else
    {
      doc.DueDate = date.AddDays(offer.Supplier.PaymentDelayDays);
      doc.ExternalNumber = random.ReplaceNumbers("######");
    }

    doc.Note = random.Bool(0.2f) ? scope.Faker.PickRandom(scope.Texts.PurchaseNotes) : null;
    return (doc, LinesOf(scope, doc, Pick(random, offer.Products).Select(product => (product, (decimal)random.Int(MinPurchaseQty, MaxPurchaseQty)))));
  }

  /// <summary>
  /// A stock issue: one to <see cref="MaxLines"/> of the products a warehouse holds on <paramref name="date"/>, each
  /// taking no more than is free; none when the warehouse has nothing free.
  /// </summary>
  private static (EcomDoc Doc, EcomDocLine[] Lines)? Issue(EcomStockScope scope, DateOnly date, EcomSeedBooks books)
  {
    var random = scope.Faker.Random;
    var shelf = random.ArrayElement(scope.Shelves);
    if (FreeOf(scope, shelf, books) is not { Count: > 0 } held)
    {
      return null;
    }

    var doc = NewDoc(scope, EcomData.DocType.StockIssueId, shelf, date);
    doc.Note = random.Bool(0.6f) ? scope.Faker.PickRandom(scope.Texts.IssueNotes) : null;
    return (doc, LinesOf(scope, doc, Pick(random, held).Select(of => (of.Product, (decimal)random.Int(1, Math.Min(MaxIssueQty, of.Free))))));
  }

  /// <summary>
  /// A stock transfer between two warehouses of one organisation: one to <see cref="MaxLines"/> of the products the
  /// first holds on <paramref name="date"/>, each moving no more than is free; none when no organisation has two
  /// warehouses or the first has nothing free.
  /// </summary>
  private static (EcomDoc Doc, EcomDocLine[] Lines)? Transfer(EcomStockScope scope, DateOnly date, EcomSeedBooks books)
  {
    if (scope.Neighbours.Length == 0)
    {
      return null;
    }

    var random = scope.Faker.Random;
    var neighbours = random.ArrayElement(scope.Neighbours);
    var from = random.Int(0, neighbours.Length - 1);
    // The second warehouse is one of the others: a step of at least one place round the organisation's warehouses
    var to = (from + random.Int(1, neighbours.Length - 1)) % neighbours.Length;
    var shelf = neighbours[from];
    if (FreeOf(scope, shelf, books) is not { Count: > 0 } held)
    {
      return null;
    }

    var doc = NewDoc(scope, EcomData.DocType.StockTransferId, shelf, date);
    doc.WarehouseToId = neighbours[to].WarehouseId;
    doc.Note = random.Bool(0.3f) ? scope.Faker.PickRandom(scope.Texts.TransferNotes) : null;
    return (doc, LinesOf(scope, doc, Pick(random, held).Select(of => (of.Product, (decimal)random.Int(1, Math.Min(MaxTransferQty, of.Free))))));
  }

  /// <summary>A document of <paramref name="shelf"/>'s organisation and warehouse, in that organisation's currency, with a manager of the tenant.</summary>
  private static EcomDoc NewDoc(EcomStockScope scope, long typeId, EcomStockShelf shelf, DateOnly date) =>
    new()
    {
      TenantId = scope.TenantId,
      OrganizationId = shelf.OrganizationId,
      DocTypeId = typeId,
      Date = date,
      WarehouseId = shelf.WarehouseId,
      ManagerId = OneOf(scope.Faker.Random, scope.Managers),
      CurrencyId = shelf.CurrencyId
    };

  /// <summary>
  /// The lines of <paramref name="doc"/>, one per product of <paramref name="products"/> with its quantity, at the
  /// product's purchase price without a discount, in steps of <see cref="SortStep"/>.
  /// </summary>
  private static EcomDocLine[] LinesOf(EcomStockScope scope, EcomDoc doc, IEnumerable<(EcomStockProduct Product, decimal Qty)> products) =>
  [
    .. products.Select((of, index) => new EcomDocLine
    {
      TenantId = scope.TenantId,
      Doc = doc,
      SortOrder = (index + 1) * SortStep,
      ProductId = of.Product.Id,
      Qty = of.Qty,
      Price = of.Product.PurchasePrice,
      Amount = EcomDocTotals.LineAmount(of.Qty, of.Product.PurchasePrice, 0m)
    })
  ];

  /// <summary>
  /// The products <paramref name="shelf"/>'s warehouse has at least one whole unit of to give by <paramref name="books"/>,
  /// free and not asked by a completed order still to ship, each with those whole units, in the order the warehouse
  /// first got them.
  /// </summary>
  private static List<(EcomStockProduct Product, int Free)> FreeOf(EcomStockScope scope, EcomStockShelf shelf, EcomSeedBooks books) =>
  [
    .. books.Held(shelf.WarehouseId)
      .Select(product => (
        Product: scope.Products[product],
        Free: (int)decimal.Floor(books.Free(new EcomStockKey(shelf.OrganizationId, shelf.WarehouseId, product)))
      ))
      .Where(of => of.Free >= 1)
  ];

  /// <summary>One to <see cref="MaxLines"/> of <paramref name="from"/>, or as many as it has, none of them twice.</summary>
  private static List<T> Pick<T>(Randomizer random, IReadOnlyList<T> from)
  {
    var count = Math.Min(random.Int(1, MaxLines), from.Count);
    var places = new List<int>(count);
    while (places.Count < count)
    {
      var place = random.Int(0, from.Count - 1);
      if (!places.Contains(place))
      {
        places.Add(place);
      }
    }

    return [.. places.Select(place => from[place])];
  }

  /// <summary>One of <paramref name="keys"/>, or none when there is no key to name.</summary>
  private static long? OneOf(Randomizer random, long[] keys) => keys.Length > 0 ? random.ArrayElement(keys) : null;
}
