namespace DemoEcom.Data.Documents;

/// <summary>
/// The stock register: the moves a document holds in a status, their sums, the reserve a customer order takes of the free
/// stock and gives back when the stock it stood on is taken away, and the posting that makes the stored moves and the
/// balances follow a status. A document's type says which rows a line gives and the status's flags say when: the stock
/// rows while the status is posted, the promise rows, reserved and expected, while it is posted and not final.
/// </summary>
public static class EcomStock
{
  /// <summary>
  /// The moves <paramref name="doc"/> holds in <paramref name="status"/>, one per line and warehouse, not added to any
  /// context. While the status is posted a stock receipt, a supplier invoice and a sales return add the line's quantity
  /// to the document's warehouse, a stock issue, a sales invoice and a supplier return take it, and a stock transfer takes
  /// it from the warehouse and adds it to the second one; while it is posted and not final a purchase order expects there
  /// what the line asks less what is done, never less than nothing, and a customer order reserves there what its line
  /// holds, <see cref="EcomDocLine.QtyReserved"/>, a line that holds nothing giving no row. So a sales invoice ready to
  /// ship, which is not posted, moves nothing, and a completed customer order holds no reserve. Any other type and any
  /// other status give no move. A row of three zeros is not written. Each move carries the document's tenant, organisation
  /// and date, and the document and the line themselves beside their ids, so a graph not saved yet is added whole. It
  /// reads no database and throws when a row needs a warehouse the document does not name.
  /// </summary>
  public static IReadOnlyList<EcomStockMove> MovesOf(EcomDoc doc, IReadOnlyCollection<EcomDocLine> lines, EcomDocStatus status)
  {
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(lines);
    ArgumentNullException.ThrowIfNull(status);
    var moves = new List<EcomStockMove>();
    foreach (var line in lines)
    {
      foreach (var (warehouseId, qty) in RowsOf(doc, line, status))
      {
        if (warehouseId is not { } warehouse)
        {
          throw new InvalidOperationException($"The document '{doc.Number}' moves stock and names no warehouse for it.");
        }

        if (qty.IsZero)
        {
          continue;
        }

        moves.Add(
          new EcomStockMove
          {
            TenantId = doc.TenantId,
            Doc = doc,
            DocId = doc.Id,
            DocLine = line,
            DocLineId = line.Id == 0 ? null : line.Id,
            Date = doc.Date,
            OrganizationId = doc.OrganizationId,
            WarehouseId = warehouse,
            ProductId = line.ProductId,
            Qty = qty.Qty,
            Reserved = qty.Reserved,
            Expected = qty.Expected
          }
        );
      }
    }

    return moves;
  }

  /// <summary>
  /// The sums of <paramref name="moves"/> per organisation, warehouse and product: what the balances of those moves
  /// alone would hold. A key whose moves cancel out stays, with three zeros.
  /// </summary>
  public static Dictionary<EcomStockKey, EcomStockQty> Sum(IEnumerable<EcomStockMove> moves)
  {
    ArgumentNullException.ThrowIfNull(moves);
    var sums = new Dictionary<EcomStockKey, EcomStockQty>();
    foreach (var move in moves)
    {
      var key = new EcomStockKey(move.OrganizationId, move.WarehouseId, move.ProductId);
      sums[key] = sums.GetValueOrDefault(key).Plus(new EcomStockQty(move.Qty, move.Reserved, move.Expected));
    }

    return sums;
  }

  /// <summary>
  /// Gives the lines of a customer order their reserve, <see cref="EcomDocLine.QtyReserved"/>, out of
  /// <paramref name="free"/>: the free stock of the order's warehouse by product, in which what the order's own lines hold
  /// does not count; a product it does not name has none. The lines of a product are served in their order,
  /// <see cref="EcomDocLine.SortOrder"/> and then the id, first come first served: what is available to them is the free
  /// stock and what the order's lines of the product already hold, each line takes what it still needs of it, its quantity
  /// less what is done and never less than nothing, and what it takes is no longer available to the next. So what a line
  /// lacks stays open on it, its quantity less the done and the reserved, and the order never ends holding less of a
  /// product than it held and still needs, whatever became of the free stock: a free stock below zero counts as none. It
  /// changes the lines alone and reads no database.
  /// </summary>
  public static void Reserve(IReadOnlyList<EcomDocLine> lines, IReadOnlyDictionary<long, decimal> free)
  {
    ArgumentNullException.ThrowIfNull(lines);
    ArgumentNullException.ThrowIfNull(free);
    foreach (var product in lines.GroupBy(line => line.ProductId))
    {
      var available = Math.Max(free.GetValueOrDefault(product.Key), 0m) + product.Sum(line => line.QtyReserved);
      foreach (var line in product.OrderBy(line => line.SortOrder).ThenBy(line => line.Id))
      {
        line.QtyReserved = Math.Min(Math.Max(line.Qty - line.QtyDone, 0m), available);
        available -= line.QtyReserved;
      }
    }
  }

  /// <summary>
  /// <see cref="Reserve"/> over the free stock of the warehouse of <paramref name="order"/>: the balances of the lines'
  /// products there are read as the posting reads them, tracked, so a balance an earlier document changed or added in this
  /// context counts as staged and the second order of a batch reserves what the first left free. The free stock of a
  /// product is its balance's quantity less its reserved, which holds the order's own reserve as its lines carry it; a
  /// product without a balance has none. An order that names no warehouse reserves nothing: its lines hold zero. It
  /// changes the lines alone: <see cref="PostAsync(EcomDbContext, EcomDoc, EcomDocStatus, CancellationToken, IReadOnlyDictionary{EcomStockKey, decimal})"/>
  /// writes the reserve into the moves and the balances.
  /// </summary>
  public static async Task ReserveAsync(EcomDbContext db, EcomDoc order, IReadOnlyList<EcomDocLine> lines, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(order);
    ArgumentNullException.ThrowIfNull(lines);
    if (order.WarehouseId is not { } warehouse)
    {
      foreach (var line in lines)
      {
        line.QtyReserved = 0m;
      }

      return;
    }

    var balances = await BalancesAsync(db, lines.Select(line => new EcomStockKey(order.OrganizationId, warehouse, line.ProductId)), cancellationToken);
    Reserve(lines, balances.ToDictionary(of => of.Key.ProductId, of => of.Value.Qty - of.Value.Reserved));
  }

  /// <summary>
  /// Lowers what the lines of a customer order hold, <see cref="EcomDocLine.QtyReserved"/>, by what the stock no longer
  /// covers: <paramref name="lacking"/> is, by product, what the balance of the order's warehouse lacks, its reserved less
  /// its quantity, above zero once more is reserved of the product than is there. The lines of a product give back in
  /// the reverse of the order <see cref="Reserve"/> serves them in, the last line first: each gives what the product
  /// still lacks, at most what it holds. So the order keeps every hold the stock still covers, and gives back no more
  /// than is lacking. A product that lacks nothing, and one the map does not name, leaves its lines as they are. It
  /// changes the lines alone and reads no database.
  /// </summary>
  public static void Release(IReadOnlyList<EcomDocLine> lines, IReadOnlyDictionary<long, decimal> lacking)
  {
    ArgumentNullException.ThrowIfNull(lines);
    ArgumentNullException.ThrowIfNull(lacking);
    foreach (var product in lines.GroupBy(line => line.ProductId))
    {
      var rest = lacking.GetValueOrDefault(product.Key);
      foreach (var line in product.OrderByDescending(line => line.SortOrder).ThenByDescending(line => line.Id))
      {
        if (rest <= 0m)
        {
          break;
        }

        var given = Math.Min(line.QtyReserved, rest);
        line.QtyReserved -= given;
        rest -= given;
      }
    }
  }

  /// <summary>
  /// <see cref="Release"/> over the balances of the warehouse of <paramref name="order"/>: the balances of the products
  /// its lines hold are read as the posting reads them, tracked, so a balance an earlier document changed in this context
  /// counts as staged: the stock a supplier invoice taken back just took away, and the reserve the order released before
  /// this one already gave back. What a product lacks is its balance's reserved less its quantity, which holds the
  /// order's own reserve as its lines carry it. An order that holds nothing, or names no warehouse, reads nothing. It
  /// changes the lines alone: <see cref="PostAsync(EcomDbContext, EcomDoc, EcomDocStatus, CancellationToken, IReadOnlyDictionary{EcomStockKey, decimal})"/>
  /// writes the lower reserve into the moves and the balances.
  /// </summary>
  public static async Task ReleaseAsync(EcomDbContext db, EcomDoc order, IReadOnlyList<EcomDocLine> lines, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(order);
    ArgumentNullException.ThrowIfNull(lines);
    if (order.WarehouseId is not { } warehouse)
    {
      return;
    }

    var held = lines.Where(line => line.QtyReserved > 0m).Select(line => new EcomStockKey(order.OrganizationId, warehouse, line.ProductId));
    var balances = await BalancesAsync(db, held, cancellationToken);
    Release(lines, balances.ToDictionary(of => of.Key.ProductId, of => of.Value.Reserved - of.Value.Qty));
  }

  /// <summary>
  /// Makes the stored moves of <paramref name="doc"/> and the balances they sum into those of <paramref name="status"/>,
  /// or returns why not and stages nothing. A document that names no warehouse for a row the status gives is refused
  /// first, <see cref="EcomDocRefusal.NoWarehouse"/>, before anything is read. When the moves change, one whose warehouse
  /// is not of the document's organisation is refused next, <see cref="EcomDocRefusal.ForeignWarehouse"/>, before a
  /// balance is read: no key of the database compares the organisations, so the posting, the one writer of the register,
  /// does. A status that gives no move is not asked, so such a document is still taken back. The document's moves, the
  /// stored ones and the ones this context staged, are replaced by <see cref="MovesOf"/> over its lines as this context's
  /// save would leave them, and every balance takes
  /// the difference of the two sums; a balance not kept yet is added, and one that comes back to zero stays. Moves that
  /// are already those of the status are left as they are. Before anything is staged every balance whose quantity falls
  /// or whose reserve grows is checked: the quantity never goes below zero, and never below the reserved. One that would
  /// go below gives a refusal <see cref="EcomDocRefusal.StockShort"/> per line of the document that names its product,
  /// with the product and the missing quantity. <paramref name="held"/> is the reserve another document holds for this
  /// one, by balance: the stock this document may take although it is reserved, so the quantity less the reserved may go
  /// below zero by as much as is held, and the quantity itself never below zero; the caller then releases that reserve in
  /// the same save. Without it nothing reserved is taken. A balance an earlier document staged in this context counts as
  /// staged. It stages only: the caller saves. The screens take the lock of the organisation's register before the
  /// change of status that calls this, so a second posting at once waits and is checked against what the first left; a
  /// posting saved outside that lock fails by the balance's version, or by the unique index of the balances when both
  /// added the first one.
  /// </summary>
  public static async Task<IReadOnlyList<EcomDocRefusal>> PostAsync(
    EcomDbContext db,
    EcomDoc doc,
    EcomDocStatus status,
    CancellationToken cancellationToken,
    IReadOnlyDictionary<EcomStockKey, decimal>? held = null
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    return await PostAsync(db, doc, await EcomDocTotals.LinesAsync(db, doc, cancellationToken), status, cancellationToken, held);
  }

  /// <summary>
  /// <see cref="PostAsync(EcomDbContext, EcomDoc, EcomDocStatus, CancellationToken, IReadOnlyDictionary{EcomStockKey, decimal})"/>
  /// over <paramref name="lines"/>, the document's lines as the caller already loaded them into this context.
  /// </summary>
  internal static async Task<IReadOnlyList<EcomDocRefusal>> PostAsync(
    EcomDbContext db,
    EcomDoc doc,
    IReadOnlyList<EcomDocLine> lines,
    EcomDocStatus status,
    CancellationToken cancellationToken,
    IReadOnlyDictionary<EcomStockKey, decimal>? held = null
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(lines);
    ArgumentNullException.ThrowIfNull(status);
    if (lines.Any(line => RowsOf(doc, line, status).Any(row => row.WarehouseId is null)))
    {
      return [new EcomDocRefusal(EcomDocRefusal.NoWarehouse)];
    }

    var old = await MovesAsync(db, doc, cancellationToken);
    var next = MovesOf(doc, lines, status);
    if (Same(old, next))
    {
      return [];
    }

    if (await ForeignAsync(db, doc, next, cancellationToken))
    {
      return [new EcomDocRefusal(EcomDocRefusal.ForeignWarehouse)];
    }

    var delta = Sum(next);
    foreach (var (key, qty) in Sum(old))
    {
      delta[key] = delta.GetValueOrDefault(key).Minus(qty);
    }

    // In key order, so every posting adds and updates the balances in one order
    var changed = delta.Where(of => !of.Value.IsZero).OrderBy(of => of.Key.OrganizationId).ThenBy(of => of.Key.WarehouseId)
      .ThenBy(of => of.Key.ProductId).ToList();
    var balances = await BalancesAsync(db, changed.Select(of => of.Key), cancellationToken);

    // What each product lacks, checked before anything is staged: a refused posting leaves the context as it was
    var lacking = new Dictionary<long, decimal>();
    foreach (var (key, qty) in changed.Where(of => of.Value.Qty < 0m || of.Value.Reserved > 0m))
    {
      var kept = balances.TryGetValue(key, out var balance) ? new EcomStockQty(balance.Qty, balance.Reserved, balance.Expected) : default;
      var after = kept.Plus(qty);
      var missing = -Math.Min(after.Qty, after.Free + (held?.GetValueOrDefault(key) ?? 0m));
      if (missing > 0m)
      {
        lacking[key.ProductId] = Math.Max(lacking.GetValueOrDefault(key.ProductId), missing);
      }
    }

    if (lacking.Count > 0)
    {
      return await RefusalsAsync(db, lines, lacking, cancellationToken);
    }

    db.EcomStockMove.RemoveRange(old);
    db.EcomStockMove.AddRange(next);
    foreach (var (key, qty) in changed)
    {
      if (!balances.TryGetValue(key, out var balance))
      {
        balance = new EcomStockBalance
        {
          TenantId = doc.TenantId, OrganizationId = key.OrganizationId, WarehouseId = key.WarehouseId, ProductId = key.ProductId
        };
        db.EcomStockBalance.Add(balance);
      }

      balance.Qty += qty.Qty;
      balance.Reserved += qty.Reserved;
      balance.Expected += qty.Expected;
    }

    return [];
  }

  /// <summary>
  /// The rows <paramref name="line"/> of <paramref name="doc"/> gives in <paramref name="status"/>, each a warehouse of
  /// the document and what the line changes there: the one table of which type moves what, and while which status. A row
  /// names no warehouse when the document does not name the one it needs.
  /// </summary>
  private static IEnumerable<(long? WarehouseId, EcomStockQty Qty)> RowsOf(EcomDoc doc, EcomDocLine line, EcomDocStatus status)
  {
    var stock = status.IsPosted;
    var promise = status is { IsPosted: true, IsFinal: false };
    switch (doc.DocTypeId)
    {
      case EcomData.DocType.StockReceiptId or EcomData.DocType.SupplierInvoiceId or EcomData.DocType.SalesReturnId when stock:
        yield return (doc.WarehouseId, new EcomStockQty(line.Qty, 0m, 0m));
        break;
      case EcomData.DocType.StockIssueId or EcomData.DocType.SalesInvoiceId or EcomData.DocType.SupplierReturnId when stock:
        yield return (doc.WarehouseId, new EcomStockQty(-line.Qty, 0m, 0m));
        break;
      case EcomData.DocType.StockTransferId when stock:
        yield return (doc.WarehouseId, new EcomStockQty(-line.Qty, 0m, 0m));
        yield return (doc.WarehouseToId, new EcomStockQty(line.Qty, 0m, 0m));
        break;
      case EcomData.DocType.PurchaseOrderId when promise:
        yield return (doc.WarehouseId, new EcomStockQty(0m, 0m, Math.Max(line.Qty - line.QtyDone, 0m)));
        break;
      // What the line holds is set before the posting, by Reserve: a line that holds nothing asks for no warehouse
      case EcomData.DocType.CustomerOrderId when promise && line.QtyReserved > 0m:
        yield return (doc.WarehouseId, new EcomStockQty(0m, line.QtyReserved, 0m));
        break;
    }
  }

  /// <summary>
  /// The moves of <paramref name="doc"/> as this context's save would leave them: the stored ones are loaded into the
  /// context, then the tracked ones are read without those marked deleted, so moves staged and not saved yet count.
  /// </summary>
  private static async Task<List<EcomStockMove>> MovesAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken)
  {
    if (doc.Id != 0 && db.Entry(doc).State != EntityState.Added)
    {
      await db.EcomStockMove.Where(move => move.DocId == doc.Id).LoadAsync(cancellationToken);
    }

    return [.. db.EcomStockMove.Local.Where(move => move.Doc == doc || doc.Id != 0 && move.DocId == doc.Id)];
  }

  /// <summary>
  /// The balances of <paramref name="keys"/> that exist, tracked: the stored ones read in one query, and those this
  /// context added and has not saved yet, which no query returns.
  /// </summary>
  private static async Task<Dictionary<EcomStockKey, EcomStockBalance>> BalancesAsync(
    EcomDbContext db,
    IEnumerable<EcomStockKey> keys,
    CancellationToken cancellationToken
  )
  {
    var wanted = keys.ToHashSet();
    if (wanted.Count == 0)
    {
      return [];
    }

    var organizations = wanted.Select(key => key.OrganizationId).Distinct().ToList();
    var warehouses = wanted.Select(key => key.WarehouseId).Distinct().ToList();
    var products = wanted.Select(key => key.ProductId).Distinct().ToList();
    await db.EcomStockBalance.Where(
      balance => organizations.Contains(balance.OrganizationId) && warehouses.Contains(balance.WarehouseId) && products.Contains(balance.ProductId)
    ).LoadAsync(cancellationToken);
    return db.EcomStockBalance.Local.Select(balance => (Key: new EcomStockKey(balance.OrganizationId, balance.WarehouseId, balance.ProductId), balance))
      .Where(of => wanted.Contains(of.Key)).ToDictionary(of => of.Key, of => of.balance);
  }

  /// <summary>
  /// A refusal per line of <paramref name="lines"/> that names a product of <paramref name="lacking"/>, in the lines'
  /// order, with the product as its article and its name and the quantity it lacks; a product no line names is refused
  /// once, without a line.
  /// </summary>
  private static async Task<IReadOnlyList<EcomDocRefusal>> RefusalsAsync(
    EcomDbContext db,
    IReadOnlyList<EcomDocLine> lines,
    Dictionary<long, decimal> lacking,
    CancellationToken cancellationToken
  )
  {
    var ids = lacking.Keys.ToList();
    var names = await db.OrgProduct.AsNoTracking().Where(product => ids.Contains(product.Id))
      .Select(product => new { product.Id, product.Article, product.Name }).ToDictionaryAsync(
        product => product.Id,
        product => $"{product.Article} {product.Name}",
        cancellationToken
      );
    var refusals = lines.Where(line => lacking.ContainsKey(line.ProductId)).OrderBy(line => line.SortOrder).ThenBy(line => line.Id)
      .Select(line => Refusal(line.ProductId, line.Id == 0 ? null : line.Id)).ToList();
    refusals.AddRange(ids.Where(id => lines.All(line => line.ProductId != id)).Order().Select(id => Refusal(id, null)));
    return refusals;

    EcomDocRefusal Refusal(long productId, long? lineId) =>
      new(EcomDocRefusal.StockShort, lineId, names.GetValueOrDefault(productId), lacking[productId]);
  }

  /// <summary>
  /// Whether a warehouse of <paramref name="next"/>, the moves the new status gives, is not one of the organisation of
  /// <paramref name="doc"/>: one read counts the named warehouses that are, so a warehouse no row answers, one of another
  /// tenant among them, is foreign too. No move asks nothing and reads nothing.
  /// </summary>
  private static async Task<bool> ForeignAsync(
    EcomDbContext db,
    EcomDoc doc,
    IReadOnlyList<EcomStockMove> next,
    CancellationToken cancellationToken
  )
  {
    var warehouses = next.Select(move => move.WarehouseId).Distinct().ToList();
    if (warehouses.Count == 0)
    {
      return false;
    }

    var own = await db.OrgWarehouse.CountAsync(
      warehouse => warehouses.Contains(warehouse.Id) && warehouse.OrganizationId == doc.OrganizationId, cancellationToken);
    return own != warehouses.Count;
  }

  /// <summary>Whether the two lists hold the same rows, in any order: the line, the date, the place and the quantities of each.</summary>
  private static bool Same(List<EcomStockMove> old, IReadOnlyList<EcomStockMove> next) =>
    old.Count == next.Count && old.Select(Row).Order().SequenceEqual(next.Select(Row).Order());

  private static (long, DateOnly, long, long, long, decimal, decimal, decimal) Row(EcomStockMove move) =>
    (move.DocLine?.Id ?? move.DocLineId ?? 0, move.Date, move.OrganizationId, move.WarehouseId, move.ProductId, move.Qty, move.Reserved,
      move.Expected);
}
