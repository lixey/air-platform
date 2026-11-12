using DemoEcom.Data.Documents.Internals;

namespace DemoEcom.Data.Documents;

/// <summary>
/// Purchase-order entry on customer-order shortages, <see cref="EcomDocLine.QtyPurchased"/> recalculation, and
/// waiting-order reserve resolution for supplier-invoice arrivals.
/// </summary>
public static class EcomDocPurchasing
{
  /// <summary>
  /// What is still to be purchased for <paramref name="line"/> of a customer order: its shortage less what active
  /// purchase orders already ask for it (<see cref="EcomDocLine.QtyPurchased"/>), never below zero.
  /// </summary>
  public static decimal ToPurchase(EcomDocLine line)
  {
    ArgumentNullException.ThrowIfNull(line);
    return Math.Max(Math.Max(line.Qty - line.QtyDone - line.QtyReserved, 0m) - line.QtyPurchased, 0m);
  }

  /// <summary>
  /// Builds unsaved purchase orders in <c>New</c> status grouped by customer-order organization, product supplier, and
  /// customer-order warehouse for all positive quantities in <paramref name="wanted"/>, updating each source order line's
  /// <see cref="EcomDocLine.QtyPurchased"/>.
  /// </summary>
  public static IReadOnlyList<EcomDocEntry> EnterPurchases(IReadOnlyList<EcomPurchase> wanted, long? managerId, DateOnly date)
  {
    ArgumentNullException.ThrowIfNull(wanted);
    if (wanted.FirstOrDefault(of => of.Product.Id != of.Line.ProductId) is { } other)
    {
      throw new ArgumentException(
        $"The product {other.Product.Id} is not the product {other.Line.ProductId} of the order line it is wanted for.",
        nameof(wanted)
      );
    }

    var places = new Dictionary<EcomDoc, int>(ReferenceEqualityComparer.Instance);
    foreach (var of in wanted)
    {
      places.TryAdd(of.Order, places.Count);
    }

    var entries = new List<EcomDocEntry>();
    var purchases = wanted.Where(of => of.Qty > 0m).GroupBy(of => (of.Order.OrganizationId, of.Product.SupplierId, of.Order.WarehouseId))
      .OrderBy(of => of.Key.OrganizationId).ThenBy(of => of.Key.SupplierId).ThenBy(of => of.Key.WarehouseId);
    foreach (var purchase in purchases)
    {
      var tenant = purchase.First().Order.TenantId;
      var doc = new EcomDoc
      {
        TenantId = tenant,
        OrganizationId = purchase.Key.OrganizationId,
        DocTypeId = EcomDocTypeData.PurchaseOrderId,
        DocStatusId = EcomDocStatusData.PurchaseOrderNewId,
        Date = date,
        CounterpartyId = purchase.Key.SupplierId,
        WarehouseId = purchase.Key.WarehouseId,
        CurrencyId = purchase.First().Order.CurrencyId,
        ManagerId = managerId
      };

      var lines = new List<EcomDocLine>();
      var rows = new List<EcomDocLink>();
      var parts = new List<EcomDocLink>();
      var ordered = purchase.OrderBy(of => of.Order.Id).ThenBy(of => places[of.Order]).ThenBy(of => of.Line.SortOrder)
        .ThenBy(of => of.Line.Id);
      foreach (var of in ordered)
      {
        var line = new EcomDocLine
        {
          TenantId = tenant,
          Doc = doc,
          SortOrder = (lines.Count + 1) * 10,
          ProductId = of.Line.ProductId,
          Qty = of.Qty,
          Price = of.Product.PurchasePrice,
          Amount = EcomDocTotals.LineAmount(of.Qty, of.Product.PurchasePrice, 0m)
        };
        lines.Add(line);
        rows.Add(EcomDocEntering.Link(of.Order, of.Line, doc, line, of.Qty, null));

        if (parts.Count == 0 || !EcomDocEntering.SameSource(parts[^1], rows[^1]))
        {
          parts.Add(EcomDocEntering.Link(of.Order, null, doc, null, 0m, 0m));
        }

        parts[^1].Qty += line.Qty;
        parts[^1].Amount += line.Amount;
        of.Line.QtyPurchased += of.Qty;
      }

      EcomDocTotals.Apply(doc, lines);
      entries.Add(new EcomDocEntry(doc, lines, [.. parts, .. rows]));
    }

    return entries;
  }

  /// <summary>
  /// Validates a purchase-order line against its linked customer-order line, updates the link quantity, and stages
  /// recalculated <see cref="EcomDocLine.QtyPurchased"/> on the customer-order line.
  /// </summary>
  public static async Task<string?> PurchaseLineAsync(EcomDbContext db, EcomDocLine line, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(line);
    if (line.Id == 0)
    {
      return null;
    }

    var link = await db.EcomDocLink.Include(row => row.SourceLine).Where(row => row.TargetLineId == line.Id && row.SourceLineId != null)
      .OrderBy(row => row.Id).FirstOrDefaultAsync(cancellationToken);
    if (link?.SourceLine is not { } source)
    {
      return null;
    }

    if (line.ProductId != source.ProductId)
    {
      return EcomDocRefusal.SourceProduct;
    }

    link.Qty = line.Qty;
    await PurchasedAsync(db, [line.Id], [], cancellationToken);
    return null;
  }

  /// <summary>Recomputes <see cref="EcomDocLine.QtyPurchased"/> on source customer-order lines from <paramref name="purchases"/>.</summary>
  public static void Purchased(IReadOnlyList<EcomDocLink> purchases, IReadOnlyCollection<long>? gone = null)
  {
    ArgumentNullException.ThrowIfNull(purchases);
    foreach (var of in purchases.Where(IsPurchaseRow).GroupBy(EcomDocEntering.Source))
    {
      of.Key.QtyPurchased = of.Sum(link => Asked(link, gone));
    }
  }

  /// <summary>Loads purchase links for customer-order lines tied to <paramref name="purchaseLineIds"/> and recomputes <see cref="Purchased"/>.</summary>
  public static async Task PurchasedAsync(
    EcomDbContext db,
    IReadOnlyCollection<long> purchaseLineIds,
    IReadOnlyCollection<long> gone,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(purchaseLineIds);
    ArgumentNullException.ThrowIfNull(gone);
    if (purchaseLineIds.Count == 0)
    {
      return;
    }

    var entered = db.EcomDocLink
      .Where(link => link.TargetLineId != null && purchaseLineIds.Contains(link.TargetLineId.Value) && link.SourceLineId != null)
      .Select(link => link.SourceLineId);
    await db.EcomDocLink
      .Where(link => link.TargetLineId != null && link.TargetDoc.DocTypeId == EcomDocTypeData.PurchaseOrderId)
      .Where(link => entered.Contains(link.SourceLineId))
      .Include(link => link.SourceLine).Include(link => link.TargetLine).Include(link => link.TargetDoc).LoadAsync(cancellationToken);

    var rows = db.EcomDocLink.Local
      .Where(link => link is { SourceLine: not null, TargetLine: not null, TargetDoc.DocTypeId: EcomDocTypeData.PurchaseOrderId })
      .ToList();
    var lines = rows.Where(link => purchaseLineIds.Contains(link.TargetLine!.Id)).Select(link => link.SourceLine!).ToHashSet();
    Purchased([.. rows.Where(link => lines.Contains(link.SourceLine!))], gone);
  }

  /// <summary>Whether a purchase order in the status still asks its supplier for what has not arrived: while New or Sent.</summary>
  internal static bool Asks(long docStatusId) =>
    docStatusId is EcomDocStatusData.PurchaseOrderNewId or EcomDocStatusData.PurchaseOrderSentId;

  /// <summary>Loads and returns confirmed/paused customer orders in the same organization and warehouse waiting on the purchase lines in <paramref name="entered"/>.</summary>
  internal static async Task<IReadOnlyList<EcomWaiting>> WaitingAsync(
    EcomDbContext db,
    EcomDoc doc,
    IReadOnlyList<EcomDocLink> entered,
    CancellationToken cancellationToken
  )
  {
    var purchased = entered.Select(link => EcomDocEntering.Source(link).Id).Where(id => id != 0).Distinct().ToList();
    if (doc.WarehouseId is not { } warehouse || purchased.Count == 0)
    {
      return [];
    }

    var ids = await db.EcomDocLink
      .Where(link => link.TargetLineId != null && purchased.Contains(link.TargetLineId.Value) && link.SourceLineId != null)
      .Where(link => link.SourceDoc.DocTypeId == EcomDocTypeData.CustomerOrderId)
      .Select(link => link.SourceDocId).Distinct().ToListAsync(cancellationToken);
    if (ids.Count == 0)
    {
      return [];
    }

    await db.EcomDocLine.Where(line => ids.Contains(line.DocId)).Include(line => line.Doc).LoadAsync(cancellationToken);
    var orders = db.EcomDoc.Local
      .Where(order => ids.Contains(order.Id) && order.OrganizationId == doc.OrganizationId && order.WarehouseId == warehouse)
      .OrderBy(order => order.Id).ToList();
    if (orders.Count == 0)
    {
      return [];
    }

    var held = orders.Select(order => order.DocStatusId).Distinct().ToList();
    var statuses = await db.EcomDocStatus.AsNoTracking().Where(status => held.Contains(status.Id)).ToDictionaryAsync(status => status.Id, cancellationToken);
    return
    [
      .. orders.Where(order => EcomDocEntering.Holds(order, statuses[order.DocStatusId]))
        .Select(order => new EcomWaiting(order, EcomDocTotals.Tracked(db, order), statuses[order.DocStatusId]))
    ];
  }

  /// <summary>Returns the reserve held by <paramref name="waiting"/> orders on the products brought by <paramref name="doc"/>.</summary>
  internal static Dictionary<EcomStockKey, decimal> Waited(EcomDoc doc, IReadOnlyList<EcomDocLine> lines, IReadOnlyList<EcomWaiting> waiting)
  {
    var held = new Dictionary<EcomStockKey, decimal>();
    if (doc.WarehouseId is not { } warehouse)
    {
      return held;
    }

    var holds = waiting.SelectMany(of => of.Lines).GroupBy(line => line.ProductId).ToDictionary(of => of.Key, of => of.Sum(line => line.QtyReserved));
    foreach (var brought in lines.GroupBy(line => line.ProductId))
    {
      var qty = Math.Min(holds.GetValueOrDefault(brought.Key), brought.Sum(line => line.Qty));
      if (qty > 0m)
      {
        held[new EcomStockKey(doc.OrganizationId, warehouse, brought.Key)] = qty;
      }
    }

    return held;
  }

  private static bool IsPurchaseRow(EcomDocLink link) =>
    (link.SourceLineId is not null || link.SourceLine is not null) && (link.TargetLineId is not null || link.TargetLine is not null)
    && TargetDoc(link).DocTypeId == EcomDocTypeData.PurchaseOrderId;

  private static decimal Asked(EcomDocLink link, IReadOnlyCollection<long>? gone)
  {
    var line = EcomDocEntering.Target(link);
    return Asks(TargetDoc(link).DocStatusId) && !(line.Id != 0 && gone?.Contains(line.Id) == true) ? Math.Max(line.Qty - line.QtyDone, 0m) : 0m;
  }

  private static EcomDoc TargetDoc(EcomDocLink link) =>
    (EcomDoc?)link.TargetDoc ?? throw new InvalidOperationException($"The target document {link.TargetDocId} of a link is not loaded.");
}
