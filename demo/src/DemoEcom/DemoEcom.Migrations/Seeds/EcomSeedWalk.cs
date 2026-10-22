namespace DemoEcom.Migrations.Seeds;

/// <summary>
/// The walk of Ecom's examples, written once at installation after Org's, one tenant after another: the customer orders
/// (<see cref="EcomExampleSeed"/>), the stock documents (<see cref="EcomStockSeed"/>), the sales
/// (<see cref="EcomSalesSeed"/>), the purchasing (<see cref="EcomPurchasingSeed"/>) and the money
/// (<see cref="EcomMoneySeed"/>) in one list of steps, walked once in date order. The dates are counted back from the tenant's reference day (<see cref="EcomSeedClock.Today"/>), the
/// day of the moment the installation gives by the tenant's own clock. A tenant's orders are generated first and held unsaved, so the demand of the completed ones is known
/// before the first document is written: the opening stock receipts are sized to it, and the books of the walk
/// (<see cref="EcomSeedBooks"/>) keep what of it is still to ship beside what every warehouse holds on the date the walk
/// has reached. On one date the capital the organisations open with stands first, then the stock documents, then the
/// orders, each with its sale and with what is purchased for it, then the returns; within those, the order they were
/// planned in. The payments of a step's documents are written with them, after them. Each of the five has a random
/// generator of its own, seeded alike, so none moves the rows of another and the same random seed writes the same rows.
/// The documents are inserted in batches, each with its lines, its moves and its links; then the balances the books
/// summed, of the stock and of the money, and last the counters of every type.
/// </summary>
internal static class EcomSeedWalk
{
  /// <summary>The documents one batch inserts with their lines, moves and links, and the balances one batch inserts.</summary>
  private const int BatchSize = 500;

  private const int BalanceBatchSize = 5000;

  /// <summary>
  /// Writes the examples of <paramref name="tenants"/>, each dated before its own day of <paramref name="now"/>, the
  /// moment the installation runs. Without <paramref name="purchasing"/> nothing is purchased for the short orders:
  /// every other row is the one the whole walk writes, but for the reserve an arrival brings. Without
  /// <paramref name="payments"/> no payment is written and nothing is paid: every other row is the one the whole walk
  /// writes, but for the paid amount of a document.
  /// </summary>
  public static async Task RunAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int seed,
    IReadOnlyList<AppSeedTenant> tenants,
    DateTimeOffset now,
    bool purchasing,
    bool payments,
    CancellationToken cancellationToken
  )
  {
    var (orderRandom, stockRandom, salesRandom, purchaseRandom, moneyRandom) =
      (new Randomizer(seed), new Randomizer(seed), new Randomizer(seed), new Randomizer(seed), new Randomizer(seed));
    var purchaseTurns = new EcomPurchasingTurns();
    var moneyTurns = new EcomMoneyTurns();
    foreach (var tenant in tenants)
    {
      var tenantId = tenant.Row.Id;
      var numbers = new Dictionary<(long OrganizationId, int Year), int>();
      List<(EcomDoc Doc, EcomDocLine[] Lines)> orders =
        [.. await EcomExampleSeed.OrdersAsync(db, volume, seed, orderRandom, tenant, tenants, now, numbers, cancellationToken) ?? []];
      var books = new EcomSeedBooks(
        EcomSeedClock.ZoneOf(tenant, tenants),
        await db.OrgOrganization.IgnoreQueryFilters().Where(organization => organization.TenantId == tenantId)
          .ToDictionaryAsync(organization => organization.Id, organization => organization.Code, cancellationToken),
        EcomSalesSeed.Demand(orders)
      );
      var purchases = purchasing
        ? await EcomPurchasingSeed.OfAsync(db, volume, seed, purchaseRandom, purchaseTurns, tenant, books, cancellationToken)
        : null;
      var money = payments ? await EcomMoneySeed.OfAsync(db, moneyRandom, moneyTurns, tenant, books, now, cancellationToken) : null;
      List<EcomSeedStep> steps =
      [
        .. money?.Opening() ?? [],
        .. await EcomStockSeed.StepsAsync(db, volume, seed, stockRandom, tenant, books, now, cancellationToken),
        .. EcomSalesSeed.Steps(books, orders, salesRandom, purchases)
      ];

      // The sort keeps the steps of one date and rank in the order they were planned; a step's documents are paid as
      // the walk reaches it, so a payment sees what its money account holds then
      foreach (var batch in steps.OrderBy(step => step.Date).ThenBy(step => step.Rank)
                 .SelectMany(step => money is null ? step.Write() : money.Paid(step.Write())).Chunk(BatchSize))
      {
        var links = batch.SelectMany(written => written.Links).ToList();
        links.ForEach(NameStored);
        db.EcomDoc.AddRange(batch.Select(written => written.Doc));
        db.EcomDocLine.AddRange(batch.SelectMany(written => written.Lines));
        db.EcomDocLink.AddRange(links);
        db.EcomStockMove.AddRange(batch.SelectMany(written => written.Moves));
        db.EcomMoneyMove.AddRange(batch.SelectMany(written => written.MoneyMoves));
        db.EcomDocMove.AddRange(batch.SelectMany(written => written.History));
        await db.SaveChangesAsync(true, cancellationToken);
        db.ChangeTracker.Clear();
      }

      await WriteBalancesAsync(db, tenantId, books, cancellationToken);
      await WriteMoneyBalancesAsync(db, tenantId, books, cancellationToken);
      await WriteCountersAsync(db, tenantId, numbers, books, cancellationToken);
    }
  }

  /// <summary>The balances after the last move: what the walk summed in memory is what the register holds.</summary>
  private static async Task WriteBalancesAsync(EcomDbContext db, long tenantId, EcomSeedBooks books, CancellationToken cancellationToken)
  {
    foreach (var batch in books.Stock.OrderBy(balance => balance.Key.OrganizationId).ThenBy(balance => balance.Key.WarehouseId)
               .ThenBy(balance => balance.Key.ProductId).Chunk(BalanceBatchSize))
    {
      db.EcomStockBalance.AddRange(batch.Select(balance => new EcomStockBalance
      {
        TenantId = tenantId,
        OrganizationId = balance.Key.OrganizationId,
        WarehouseId = balance.Key.WarehouseId,
        ProductId = balance.Key.ProductId,
        Qty = balance.Value.Qty,
        Reserved = balance.Value.Reserved,
        Expected = balance.Value.Expected
      }));
      await db.SaveChangesAsync(true, cancellationToken);
      db.ChangeTracker.Clear();
    }
  }

  /// <summary>The money balances after the last payment: what the walk summed in memory is what the register holds.</summary>
  private static async Task WriteMoneyBalancesAsync(EcomDbContext db, long tenantId, EcomSeedBooks books, CancellationToken cancellationToken)
  {
    db.EcomMoneyBalance.AddRange(books.Money.OrderBy(balance => balance.Key.OrganizationId).ThenBy(balance => balance.Key.MoneyAccountId)
      .Select(balance => new EcomMoneyBalance
      {
        TenantId = tenantId,
        OrganizationId = balance.Key.OrganizationId,
        MoneyAccountId = balance.Key.MoneyAccountId,
        Amount = balance.Value
      }));
    await db.SaveChangesAsync(true, cancellationToken);
    db.ChangeTracker.Clear();
  }

  /// <summary>
  /// The counters last, those of the customer orders, which <paramref name="numbers"/> keeps, with those of every other
  /// type: the next document of an organisation, a type and a year continues the sequence the seed left.
  /// </summary>
  private static async Task WriteCountersAsync(
    EcomDbContext db,
    long tenantId,
    Dictionary<(long OrganizationId, int Year), int> numbers,
    EcomSeedBooks books,
    CancellationToken cancellationToken
  )
  {
    db.EcomDocCounter.AddRange(
      numbers.Select(number => (number.Key.OrganizationId, DocTypeId: EcomData.DocType.CustomerOrderId, number.Key.Year, Last: number.Value))
        .Concat(books.Counters.Select(counter => (counter.Key.OrganizationId, counter.Key.DocTypeId, counter.Key.Year, Last: counter.Value)))
        .OrderBy(counter => counter.OrganizationId).ThenBy(counter => counter.DocTypeId).ThenBy(counter => counter.Year)
        .Select(counter => new EcomDocCounter
        {
          TenantId = tenantId,
          OrganizationId = counter.OrganizationId,
          DocTypeId = counter.DocTypeId,
          Year = counter.Year,
          LastNumber = counter.Last
        })
    );
    await db.SaveChangesAsync(true, cancellationToken);
    db.ChangeTracker.Clear();
  }

  /// <summary>
  /// Makes <paramref name="link"/> name a source end an earlier batch stored by its key alone: a stored row hung on a
  /// navigation would be added again. A source of the same batch stays on its navigation, since it has no key yet.
  /// </summary>
  private static void NameStored(EcomDocLink link)
  {
    if (link.SourceDoc is { Id: not 0 } doc)
    {
      link.SourceDocId = doc.Id;
      link.SourceDoc = null!;
    }

    if (link.SourceLine is { Id: not 0 } line)
    {
      link.SourceLineId = line.Id;
      link.SourceLine = null;
    }
  }
}
