namespace DemoEcom.Data.Documents;

/// <summary>
/// Documents entered on others: sales invoices on customer orders, sales returns on sales invoices, supplier invoices
/// on purchase orders, and supplier returns on supplier invoices; purchase-order entry and waiting-order reserve
/// tracking live in <see cref="EcomDocPurchasing"/>, while payments are entered by <see cref="EcomDocPayments"/>.
/// </summary>
public static class EcomDocEntering
{
  /// <summary>
  /// Builds an unsaved document of <paramref name="targetTypeId"/> entered on <paramref name="source"/> in
  /// <paramref name="targetStatusId"/> dated <paramref name="date"/>, or <c>null</c> when no line of
  /// <paramref name="sourceLines"/> has an <paramref name="open"/> quantity above zero.
  /// </summary>
  public static EcomDocEntry? Enter(
    EcomDoc source,
    IReadOnlyList<EcomDocLine> sourceLines,
    Func<EcomDocLine, decimal> open,
    long targetTypeId,
    long targetStatusId,
    DateOnly date
  )
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(sourceLines);
    ArgumentNullException.ThrowIfNull(open);
    var doc = new EcomDoc
    {
      TenantId = source.TenantId,
      OrganizationId = source.OrganizationId,
      DocTypeId = targetTypeId,
      DocStatusId = targetStatusId,
      Date = date,
      CustomerId = source.CustomerId,
      ContactId = source.ContactId,
      CounterpartyId = source.CounterpartyId,
      WarehouseId = source.WarehouseId,
      CurrencyId = source.CurrencyId,
      ManagerId = source.ManagerId
    };
    if (targetTypeId == EcomDocTypeData.SalesInvoiceId)
    {
      doc.DeliveryTypeId = source.DeliveryTypeId;
      doc.PaymentMethodId = source.PaymentMethodId;
      doc.PaymentTermId = source.PaymentTermId;
      doc.RecipientName = source.RecipientName;
      doc.RecipientPhone = source.RecipientPhone;
      doc.DeliveryAddress = source.DeliveryAddress;
    }

    var lines = new List<EcomDocLine>();
    var links = new List<EcomDocLink>();
    foreach (var from in sourceLines.OrderBy(line => line.SortOrder).ThenBy(line => line.Id))
    {
      var qty = open(from);
      if (qty <= 0m)
      {
        continue;
      }

      var line = new EcomDocLine
      {
        TenantId = source.TenantId,
        Doc = doc,
        SortOrder = from.SortOrder,
        ProductId = from.ProductId,
        Qty = qty,
        Price = from.Price,
        DiscountPercent = from.DiscountPercent,
        Amount = EcomDocTotals.LineAmount(qty, from.Price, from.DiscountPercent)
      };
      lines.Add(line);
      links.Add(Link(source, from, doc, line, qty, null));
    }

    if (lines.Count == 0)
    {
      return null;
    }

    EcomDocTotals.Apply(doc, lines);
    links.Insert(0, Link(source, null, doc, null, doc.Qty, doc.Amount));
    return new EcomDocEntry(doc, lines, links);
  }

  /// <summary>
  /// Returns the non-canceled target line quantities already taken from <paramref name="sourceLineIds"/>, excluding
  /// purchase orders and optionally <paramref name="exceptTargetDocId"/>.
  /// </summary>
  public static async Task<Dictionary<long, decimal>> TakenAsync(
    EcomDbContext db,
    IReadOnlyCollection<long> sourceLineIds,
    long? exceptTargetDocId,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(sourceLineIds);
    if (sourceLineIds.Count == 0)
    {
      return [];
    }

    var rows = await db.EcomDocLink.AsNoTracking().Where(
      link => link.SourceLineId != null
              && sourceLineIds.Contains(link.SourceLineId.Value)
              && (exceptTargetDocId == null || link.TargetDocId != exceptTargetDocId)
              && link.TargetDoc.DocTypeId != EcomDocTypeData.PurchaseOrderId
              && !link.TargetDoc.DocStatus.IsCanceled
    ).Select(link => new { SourceLineId = link.SourceLineId!.Value, link.Qty }).ToListAsync(cancellationToken);
    return rows.GroupBy(row => row.SourceLineId).ToDictionary(of => of.Key, of => of.Sum(row => row.Qty ?? 0m));
  }

  /// <summary>Loads and returns all document and line links targeting <paramref name="doc"/> as tracked by <paramref name="db"/>.</summary>
  public static async Task<IReadOnlyList<EcomDocLink>> BasisAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    if (doc.Id != 0 && db.Entry(doc).State != EntityState.Added)
    {
      await db.EcomDocLink.Where(link => link.TargetDocId == doc.Id).LoadAsync(cancellationToken);
    }

    return [.. db.EcomDocLink.Local.Where(link => link.TargetDoc == doc || doc.Id != 0 && link.TargetDocId == doc.Id)];
  }

  /// <summary>Whether <paramref name="doc"/> has any non-canceled non-purchase-order follower document entered on it.</summary>
  public static async Task<bool> FollowedAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    if (doc.Id != 0 && db.Entry(doc).State != EntityState.Added)
    {
      await db.EcomDocLink.Where(link => link.SourceDocId == doc.Id && link.SourceLineId == null && link.TargetLineId == null)
        .Include(link => link.TargetDoc).LoadAsync(cancellationToken);
    }

    var statuses = db.EcomDocLink.Local.Where(link => IsDocRow(link) && (link.SourceDoc == doc || doc.Id != 0 && link.SourceDocId == doc.Id))
      .Where(link => link.TargetDoc.DocTypeId != EcomDocTypeData.PurchaseOrderId)
      .Select(link => link.TargetDoc.DocStatusId).Distinct().ToList();
    return statuses.Count > 0
           && await db.EcomDocStatus.AsNoTracking().AnyAsync(status => statuses.Contains(status.Id) && !status.IsCanceled, cancellationToken);
  }

  /// <summary>Validates an entered document line against its linked source line and stages the updated link quantity.</summary>
  public static async Task<string?> LineAsync(EcomDbContext db, EcomDocLine line, CancellationToken cancellationToken)
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

    var taken = await TakenAsync(db, [source.Id], line.DocId, cancellationToken);
    if (line.Qty > source.Qty - taken.GetValueOrDefault(source.Id))
    {
      return EcomDocRefusal.OverSource;
    }

    link.Qty = line.Qty;
    return null;
  }

  /// <summary>Updates source line <see cref="EcomDocLine.QtyDone"/> and <see cref="EcomDocLine.QtyReserved"/> when a target enters or leaves posted statuses.</summary>
  public static void Fulfil(IReadOnlyList<EcomDocLink> basis, bool entering)
  {
    ArgumentNullException.ThrowIfNull(basis);
    foreach (var link in basis.Where(link => link.SourceLineId is not null || link.SourceLine is not null))
    {
      var source = Source(link);
      var qty = link.Qty ?? 0m;
      if (entering)
      {
        source.QtyDone += qty;
        source.QtyReserved -= Math.Min(source.QtyReserved, qty);
      }
      else
      {
        source.QtyDone -= qty;
      }
    }
  }

  /// <summary>Synchronizes document and line link quantities/amounts in <paramref name="basis"/> with <paramref name="doc"/>.</summary>
  public static void Follow(EcomDoc doc, IReadOnlyList<EcomDocLink> basis)
  {
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(basis);
    var parts = basis.Count(IsDocRow) > 1;
    foreach (var link in basis)
    {
      if (!IsDocRow(link))
      {
        if (link.TargetLine is { } line)
        {
          link.Qty = line.Qty;
        }
      }
      else if (parts)
      {
        var lines = basis.Where(row => !IsDocRow(row) && SameSource(row, link)).Select(Target).ToList();
        link.Qty = lines.Sum(line => line.Qty);
        link.Amount = lines.Sum(line => line.Amount);
      }
      else
      {
        link.Qty = doc.Qty;
        link.Amount = doc.Amount;
      }
    }
  }

  /// <summary>Whether a document of the type counts on the lines of its sources.</summary>
  internal static bool Fulfils(long docTypeId) =>
    docTypeId is EcomDocTypeData.SalesInvoiceId or EcomDocTypeData.SalesReturnId or EcomDocTypeData.SupplierInvoiceId
      or EcomDocTypeData.SupplierReturnId;

  /// <summary>Whether <paramref name="source"/> in <paramref name="status"/> holds a reserve.</summary>
  internal static bool Holds(EcomDoc source, EcomDocStatus status) =>
    source.DocTypeId == EcomDocTypeData.CustomerOrderId && status is { IsPosted: true, IsFinal: false };

  /// <summary>Whether <paramref name="source"/> in <paramref name="status"/> holds a promise row its lines' counters change.</summary>
  internal static bool Promises(EcomDoc source, EcomDocStatus status) =>
    source.DocTypeId is EcomDocTypeData.CustomerOrderId or EcomDocTypeData.PurchaseOrderId && status is { IsPosted: true, IsFinal: false };

  /// <summary>The rows of <paramref name="basis"/> that tie a line of the target to a line of a source, in the target lines' order.</summary>
  internal static List<EcomDocLink> Entered(IReadOnlyList<EcomDocLink> basis) =>
  [
    .. basis.Where(link => !IsDocRow(link) && (link.TargetLineId is not null || link.TargetLine is not null))
      .OrderBy(link => link.TargetLine?.SortOrder).ThenBy(link => link.TargetLine?.Id)
  ];

  /// <summary>Loads all lines of stored source documents referenced by <paramref name="entered"/> and maps each source document to its status.</summary>
  internal static async Task<Dictionary<EcomDoc, EcomDocStatus>> SourcesAsync(
    EcomDbContext db,
    IReadOnlyList<EcomDocLink> entered,
    CancellationToken cancellationToken
  )
  {
    var stored = entered.Where(link => link.SourceDocId != 0).Select(link => link.SourceDocId).Distinct().ToList();
    if (stored.Count > 0)
    {
      await db.EcomDocLine.Where(line => stored.Contains(line.DocId)).Include(line => line.Doc).LoadAsync(cancellationToken);
    }

    var docs = entered.Select(link => Source(link).Doc).Distinct().ToList();
    var ids = docs.Select(doc => doc.DocStatusId).Distinct().ToList();
    var statuses = await db.EcomDocStatus.AsNoTracking().Where(status => ids.Contains(status.Id)).ToDictionaryAsync(status => status.Id, cancellationToken);
    return docs.ToDictionary(doc => doc, doc => statuses[doc.DocStatusId]);
  }

  /// <summary>Returns <see cref="EcomDocRefusal.OverSource"/> refusals when target lines exceed their source lines' open quantities.</summary>
  internal static async Task<IReadOnlyList<EcomDocRefusal>> OverAsync(EcomDbContext db, IReadOnlyList<EcomDocLink> entered, CancellationToken cancellationToken)
  {
    var over = new List<(EcomDocLine Line, decimal By)>();
    foreach (var of in entered.GroupBy(Source))
    {
      var by = of.Sum(link => Target(link).Qty) - Math.Max(of.Key.Qty - of.Key.QtyDone, 0m);
      if (by > 0m)
      {
        over.AddRange(of.Select(link => (Target(link), by)));
      }
    }

    if (over.Count == 0)
    {
      return [];
    }

    var ids = over.Select(of => of.Line.ProductId).Distinct().ToList();
    var names = await db.OrgProduct.AsNoTracking().Where(product => ids.Contains(product.Id))
      .Select(product => new { product.Id, product.Article, product.Name }).ToDictionaryAsync(
        product => product.Id,
        product => $"{product.Article} {product.Name}",
        cancellationToken
      );
    return
    [
      .. over.OrderBy(of => of.Line.SortOrder).ThenBy(of => of.Line.Id).Select(
        of => new EcomDocRefusal(EcomDocRefusal.OverSource, of.Line.Id == 0 ? null : of.Line.Id, names.GetValueOrDefault(of.Line.ProductId), of.By)
      )
    ];
  }

  /// <summary>Returns the reserve held by same-warehouse source orders for <paramref name="doc"/>, keyed by stock balance.</summary>
  internal static Dictionary<EcomStockKey, decimal> Held(
    EcomDoc doc,
    IReadOnlyList<EcomDocLink> entered,
    IReadOnlyDictionary<EcomDoc, EcomDocStatus> sources
  )
  {
    var held = new Dictionary<EcomStockKey, decimal>();
    if (doc.WarehouseId is not { } warehouse)
    {
      return held;
    }

    foreach (var of in entered.GroupBy(Source))
    {
      var source = of.Key.Doc;
      if (!Holds(source, sources[source]) || source.OrganizationId != doc.OrganizationId || source.WarehouseId != warehouse)
      {
        continue;
      }

      var key = new EcomStockKey(doc.OrganizationId, warehouse, of.Key.ProductId);
      held[key] = held.GetValueOrDefault(key) + Math.Min(of.Key.QtyReserved, of.Sum(link => Target(link).Qty));
    }

    return held;
  }

  /// <summary>Creates a link of <paramref name="target"/> to <paramref name="source"/> using IDs for stored ends and navigations for unsaved ones.</summary>
  internal static EcomDocLink Link(EcomDoc source, EcomDocLine? sourceLine, EcomDoc target, EcomDocLine? targetLine, decimal qty, decimal? amount)
  {
    var link = new EcomDocLink { TenantId = source.TenantId, TargetDoc = target, TargetLine = targetLine, Qty = qty, Amount = amount };
    if (source.Id != 0)
    {
      link.SourceDocId = source.Id;
    }
    else
    {
      link.SourceDoc = source;
    }

    if (sourceLine is { Id: not 0 })
    {
      link.SourceLineId = sourceLine.Id;
    }
    else
    {
      link.SourceLine = sourceLine;
    }

    return link;
  }

  /// <summary>Whether the link ties the two documents and no line of either.</summary>
  internal static bool IsDocRow(EcomDocLink link) =>
    link.SourceLineId is null && link.SourceLine is null && link.TargetLineId is null && link.TargetLine is null;

  /// <summary>Whether the two links name one source document: a stored one by its id, one not saved yet by reference.</summary>
  internal static bool SameSource(EcomDocLink one, EcomDocLink other) =>
    one.SourceDoc is not null && ReferenceEquals(one.SourceDoc, other.SourceDoc)
    || one.SourceDocId != 0 && one.SourceDocId == other.SourceDocId;

  internal static EcomDocLine Source(EcomDocLink link) =>
    link.SourceLine ?? throw new InvalidOperationException($"The source line {link.SourceLineId} of a link is not loaded.");

  internal static EcomDocLine Target(EcomDocLink link) =>
    link.TargetLine ?? throw new InvalidOperationException($"The target line {link.TargetLineId} of a link is not loaded.");
}
