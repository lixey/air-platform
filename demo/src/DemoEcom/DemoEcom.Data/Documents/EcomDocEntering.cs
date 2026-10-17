namespace DemoEcom.Data.Documents;

/// <summary>
/// Documents entered on others: a sales invoice on a customer order, a sales return on a sales invoice, a purchase order
/// on the lines of customer orders, a supplier invoice on a purchase order, a supplier return on a supplier invoice; a
/// payment, which has no line, is entered by <see cref="EcomDocPayments"/> and follows and holds as the others do. The
/// links tie the two: one row document to document, which carries the target's totals, or its source's part of them
/// when the target has several sources, and a row per entered line, which carries the quantity the target's line takes
/// of the source's. A source line's <see cref="EcomDocLine.QtyDone"/> counts the line rows of its targets in a posted
/// status: what shipped of an order's line, what came back of a sales invoice's, what was received of a purchase line
/// and what was returned of a supplier invoice's. What is open to enter on it counts every target that is not canceled,
/// a draft too, so a second document entered on a source does not take what a draft already took.
/// <para>
/// A purchase order is the one target that takes nothing of its source and does not hold it: it asks a supplier for what
/// an order line lacks. What its lines ask and has not arrived is the order line's
/// <see cref="EcomDocLine.QtyPurchased"/> (<see cref="Purchased"/>), which no shipment counts and no change of the
/// customer order waits for.
/// </para>
/// <para>
/// A supplier invoice entered on a purchase order arrives for the customer orders that order was made for: its waiting
/// orders (<see cref="WaitingAsync"/>), which reserve the arrival and give it back when the invoice is taken back.
/// </para>
/// </summary>
public static class EcomDocEntering
{
  /// <summary>
  /// The document of <paramref name="targetTypeId"/> entered on <paramref name="source"/>, in
  /// <paramref name="targetStatusId"/> and dated <paramref name="date"/>, not added to any context and with no number
  /// yet; nothing when no line of <paramref name="sourceLines"/> has a quantity <paramref name="open"/> above zero. It
  /// takes the source's tenant, organisation, customer, contact, counterparty, warehouse, currency and manager, and a
  /// sales invoice also its delivery type, payment method, payment term, recipient and delivery address; the source's
  /// currency is its organisation's, so the organisation is not read, and the rate is the model's 1. Each source
  /// line with an open quantity gives a line of that quantity with the source's product, price, discount and sort order,
  /// in the source lines' order, and the amount computed of them; the document's totals are those of its lines. The links
  /// are one row document to document with the target's quantity and amount, then a row per line with the line's
  /// quantity. An end that is stored is named by its id and one that is not by its navigation, so the document, the
  /// lines and the links are saved in one flush and a stored source is never added again. It reads no database.
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
    if (targetTypeId == EcomData.DocType.SalesInvoiceId)
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
  /// What is still to be purchased for <paramref name="line"/> of a customer order: what it lacks, its quantity less the
  /// done and the reserved, less what purchase orders already ask for it, <see cref="EcomDocLine.QtyPurchased"/>; never
  /// less than nothing at either step. It reads the line alone.
  /// </summary>
  public static decimal ToPurchase(EcomDocLine line)
  {
    ArgumentNullException.ThrowIfNull(line);
    return Math.Max(Math.Max(line.Qty - line.QtyDone - line.QtyReserved, 0m) - line.QtyPurchased, 0m);
  }

  /// <summary>
  /// The purchase orders entered on the order lines <paramref name="wanted"/>, dated <paramref name="date"/>, not added
  /// to any context and with no number yet: one order in New per organisation of the customer order, supplier of the
  /// product and warehouse of the customer order, in that order of the three. It names the organisation, the supplier as
  /// its counterparty, the warehouse and <paramref name="managerId"/>, and is in the currency of the customer orders it
  /// purchases for, which are of one organisation, at the model's rate of 1; the purchase price is taken as it stands,
  /// whatever that currency. Each wanted line with a quantity above zero gives a line of that quantity with the order
  /// line's product at the product's purchase price, with no discount and the amount computed of them; the lines stand
  /// by the customer order's id, the orders not saved yet in the order they are given, then by the order line's sort
  /// order and id, and are numbered in tens. The document's totals are those of its lines.
  /// <para>
  /// The links are a document row per source order, in the lines' order, each with that order's part, the quantity and
  /// the amount of the lines entered on it, then a row per line with the line's quantity. An end that is stored is named
  /// by its id and one that is not by its navigation, as <see cref="Enter"/> names them. Each wanted order line counts
  /// the quantity in its <see cref="EcomDocLine.QtyPurchased"/>. Nothing wanted gives no order. It reads no database
  /// and throws when a wanted product is not its line's.
  /// </para>
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

    // An order not saved yet has no id to stand by: it keeps its place among the wanted ones
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
        DocTypeId = EcomData.DocType.PurchaseOrderId,
        DocStatusId = EcomData.DocStatus.PurchaseOrderNewId,
        Date = date,
        CounterpartyId = purchase.Key.SupplierId,
        WarehouseId = purchase.Key.WarehouseId,
        // A group is one organisation, so its orders hold one currency: the organisation's
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
        rows.Add(Link(of.Order, of.Line, doc, line, of.Qty, null));

        // The lines of one order stand together, so its part is the last one or a new one
        if (parts.Count == 0 || !SameSource(parts[^1], rows[^1]))
        {
          parts.Add(Link(of.Order, null, doc, null, 0m, 0m));
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
  /// What is already taken of the source lines <paramref name="sourceLineIds"/>, by line: the sum of the line rows whose
  /// target document is not canceled, a draft counting as a delivered one does. A purchase order takes nothing of the
  /// order line it is entered on: what it asks stays open to invoice. The rows of
  /// <paramref name="exceptTargetDocId"/> are left out: what the other documents took, for a check of that one. A line
  /// nothing was taken of is absent. It reads the stored rows: a target this context added or canceled and has not saved
  /// counts as stored.
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

    // Summed here: SQLite sums no decimal
    var rows = await db.EcomDocLink.AsNoTracking().Where(
      link => link.SourceLineId != null
              && sourceLineIds.Contains(link.SourceLineId.Value)
              && (exceptTargetDocId == null || link.TargetDocId != exceptTargetDocId)
              && link.TargetDoc.DocTypeId != EcomData.DocType.PurchaseOrderId
              && !link.TargetDoc.DocStatus.IsCanceled
    ).Select(link => new { SourceLineId = link.SourceLineId!.Value, link.Qty }).ToListAsync(cancellationToken);
    return rows.GroupBy(row => row.SourceLineId).ToDictionary(of => of.Key, of => of.Sum(row => row.Qty ?? 0m));
  }

  /// <summary>
  /// The basis of <paramref name="doc"/>: the links whose target it is, the document rows and the line rows, as this
  /// context's save would leave them. The stored ones are loaded into the context, then the tracked ones are read without
  /// those marked deleted, so links added and not saved yet count. A document entered on nothing has none.
  /// </summary>
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

  /// <summary>
  /// Whether <paramref name="doc"/> has a follower that holds it and is not canceled: a document entered on it, the
  /// target of a document row whose source it is, a payment among them (<see cref="EcomDocPayments"/>). A draft follower
  /// counts and a canceled one does not. A purchase order holds nothing: the customer order it was entered on leaves the
  /// posted statuses while it stands, and what it brings arrives as free stock. The followers are read as this context's save would leave them: the stored rows are loaded
  /// with their targets, so a follower this context canceled and has not saved yet no longer holds its source, and one it
  /// added does.
  /// </summary>
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
      .Where(link => link.TargetDoc.DocTypeId != EcomData.DocType.PurchaseOrderId)
      .Select(link => link.TargetDoc.DocStatusId).Distinct().ToList();
    return statuses.Count > 0
           && await db.EcomDocStatus.AsNoTracking().AnyAsync(status => statuses.Contains(status.Id) && !status.IsCanceled, cancellationToken);
  }

  /// <summary>
  /// The check of a line of an entered document before it is saved: the code of the refusal, or nothing when the line
  /// may be saved. A line with a link to a source line keeps its source's product, else
  /// <see cref="EcomDocRefusal.SourceProduct"/>, and its quantity stays within what the source line has open, its
  /// quantity less what the other documents that are not canceled took of it, else
  /// <see cref="EcomDocRefusal.OverSource"/>. A line that passes writes its quantity into its link, staged for the same
  /// save. A line without a link, one added to the document by hand or a line not saved yet, passes.
  /// </summary>
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

  /// <summary>
  /// The check of a line of a purchase order before it is saved, <paramref name="line"/> as this context tracks it: the
  /// code of the refusal, or nothing when the line may be saved. A line with a link to a line of a customer order keeps
  /// that line's product, else <see cref="EcomDocRefusal.SourceProduct"/>, and its quantity has no limit by its source:
  /// a purchase line may ask more than its order line needs. A line that passes writes its quantity into its link and
  /// recomputes the order line's <see cref="EcomDocLine.QtyPurchased"/> (<see cref="PurchasedAsync"/>), both staged for
  /// the same save. A line without a link, one added to the order by hand or a line not saved yet, passes and writes
  /// nothing.
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

  /// <summary>
  /// Counts a target on the lines of its sources: <paramref name="basis"/> is the target's links, each line row with its
  /// source line. A target entering the posted statuses adds each row's quantity to its source line's
  /// <see cref="EcomDocLine.QtyDone"/> and takes it from what the line holds reserved, never more than it holds: what
  /// shipped is reserved no more. A target leaving them takes the quantity from the done one and gives no reserve back:
  /// the source reserves again by itself. The document rows count nowhere. It changes the source lines alone, reads no
  /// database and throws when a line row's source line is not loaded.
  /// </summary>
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

  /// <summary>
  /// Makes the links <paramref name="basis"/> of <paramref name="doc"/> follow it: every line row whose target line is
  /// loaded takes that line's quantity, and the one document row of a document entered on one source takes the
  /// document's quantity and amount. A document entered on several sources, a purchase order of the lines of several
  /// customer orders, has a document row per source, and each takes its source's part: the quantities and the amounts of
  /// the document's lines entered on that source, so a line added by hand is in no part. Called where the document's
  /// totals are written. It changes the links alone and reads no database; a part throws when a target line of its
  /// source is not loaded.
  /// </summary>
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

  /// <summary>
  /// Makes the customer order lines that <paramref name="purchases"/> name count what purchase orders ask for them and
  /// has not arrived: each source line's <see cref="EcomDocLine.QtyPurchased"/> becomes, over the rows that tie it to a
  /// line of a purchase order in New or Sent, the sum of what the purchase line asks less what is done of it, never less
  /// than nothing. So a new order counts as a sent one does, a completed and a canceled one count nothing, and what
  /// arrived counts no more. A purchase line whose id is in <paramref name="gone"/>, one about to be deleted, counts
  /// nothing. A line is recomputed over the rows given alone: the caller gives every row of each order line it names.
  /// The document rows and the rows of a target that is no purchase order are skipped, so a line they alone name is
  /// left as it is. It changes the source lines alone, reads no database and throws when a source line, a target line or
  /// a target document of a row it counts is not loaded.
  /// </summary>
  public static void Purchased(IReadOnlyList<EcomDocLink> purchases, IReadOnlyCollection<long>? gone = null)
  {
    ArgumentNullException.ThrowIfNull(purchases);
    foreach (var of in purchases.Where(IsPurchaseRow).GroupBy(Source))
    {
      of.Key.QtyPurchased = of.Sum(link => Asked(link, gone));
    }
  }

  /// <summary>
  /// <see cref="Purchased"/> for the customer order lines the purchase lines <paramref name="purchaseLineIds"/> are
  /// entered on, over every purchase line of each: the rows that tie those order lines to lines of purchase orders are
  /// loaded into the context with both lines and the purchase orders, where what the context already tracks keeps its
  /// changes, then the tracked rows are read without those marked deleted. So a purchase order whose status this context
  /// changed, a line whose quantity it changed and a row it added and has not saved yet count as its save would leave
  /// them. <paramref name="gone"/> names the purchase lines about to be deleted, which count nothing. A purchase line
  /// entered on nothing changes nothing. It stages only: the caller saves.
  /// </summary>
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
      .Where(link => link.TargetLineId != null && link.TargetDoc.DocTypeId == EcomData.DocType.PurchaseOrderId)
      .Where(link => entered.Contains(link.SourceLineId))
      .Include(link => link.SourceLine).Include(link => link.TargetLine).Include(link => link.TargetDoc).LoadAsync(cancellationToken);

    // A row another read left in the context without its target document is none of these
    var rows = db.EcomDocLink.Local
      .Where(link => link is { SourceLine: not null, TargetLine: not null, TargetDoc.DocTypeId: EcomData.DocType.PurchaseOrderId })
      .ToList();
    var lines = rows.Where(link => purchaseLineIds.Contains(link.TargetLine!.Id)).Select(link => link.SourceLine!).ToHashSet();
    Purchased([.. rows.Where(link => lines.Contains(link.SourceLine!))], gone);
  }

  /// <summary>
  /// Whether a document of the type counts on the lines of its sources: a sales invoice on its customer order's, a sales
  /// return on its sales invoice's, a supplier invoice on its purchase order's and a supplier return on its supplier
  /// invoice's. A purchase order counts on nothing.
  /// </summary>
  internal static bool Fulfils(long docTypeId) =>
    docTypeId is EcomData.DocType.SalesInvoiceId or EcomData.DocType.SalesReturnId or EcomData.DocType.SupplierInvoiceId
      or EcomData.DocType.SupplierReturnId;

  /// <summary>Whether <paramref name="source"/> in <paramref name="status"/> holds a reserve: a customer order while Confirmed or Paused.</summary>
  internal static bool Holds(EcomDoc source, EcomDocStatus status) =>
    source.DocTypeId == EcomData.DocType.CustomerOrderId && status is { IsPosted: true, IsFinal: false };

  /// <summary>
  /// Whether <paramref name="source"/> in <paramref name="status"/> holds a promise row its lines' counters change, so
  /// it is posted again once a document entered on it counted on them: a customer order while Confirmed or Paused, whose
  /// reserve follows what shipped, and a purchase order while Sent, which expects what it asks less what is done.
  /// </summary>
  internal static bool Promises(EcomDoc source, EcomDocStatus status) =>
    source.DocTypeId is EcomData.DocType.CustomerOrderId or EcomData.DocType.PurchaseOrderId && status is { IsPosted: true, IsFinal: false };

  /// <summary>Whether a purchase order in the status still asks its supplier for what has not arrived: while New or Sent.</summary>
  internal static bool Asks(long docStatusId) =>
    docStatusId is EcomData.DocStatus.PurchaseOrderNewId or EcomData.DocStatus.PurchaseOrderSentId;

  /// <summary>The rows of <paramref name="basis"/> that tie a line of the target to a line of a source, in the target lines' order.</summary>
  internal static List<EcomDocLink> Entered(IReadOnlyList<EcomDocLink> basis) =>
  [
    .. basis.Where(link => !IsDocRow(link) && (link.TargetLineId is not null || link.TargetLine is not null))
      .OrderBy(link => link.TargetLine?.SortOrder).ThenBy(link => link.TargetLine?.Id)
  ];

  /// <summary>
  /// The source documents of the line rows <paramref name="entered"/>, each with its status: the lines of the stored
  /// sources are loaded into the context with their documents, every line of each, so the rows find their source lines
  /// and a source is posted over its lines without another read; a source this context already tracks keeps its changes.
  /// </summary>
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

  /// <summary>
  /// The refusals <see cref="EcomDocRefusal.OverSource"/> of the line rows <paramref name="entered"/>, or none: the
  /// target lines entered on one source line take together no more than the line has open, its quantity less what is
  /// done. Each target line over it is refused, in the lines' order, with its product as the article and the name and
  /// the quantity it is over by. The products are read only when a line is refused.
  /// </summary>
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

  /// <summary>
  /// The reserve the sources hold for <paramref name="doc"/>, by balance: for each source line of a source that holds a
  /// reserve in the document's own warehouse, what the line holds, at most what the target lines entered on it take.
  /// A source of another warehouse, or in a status that holds nothing, holds nothing for the document.
  /// </summary>
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

  /// <summary>
  /// The waiting orders of the supplier invoice <paramref name="doc"/>, those its arrival is for: the customer orders
  /// tied by a line row to the purchase lines the invoice's line rows <paramref name="entered"/> are entered on, in a
  /// status that holds a reserve, Confirmed or Paused, of the invoice's organisation and warehouse, the oldest first, by
  /// id, each with its lines and its status. The lines of the tied orders are loaded into the context with their orders,
  /// every line of each, so an order is reserved and posted over them without another read; an order this context
  /// already tracks keeps its changes, and its status, organisation and warehouse are read as its save would leave them.
  /// An invoice that names no warehouse, and one whose purchase lines were made for no order, has none. The rows read
  /// are the stored ones, and it throws when a row's source line is not loaded.
  /// </summary>
  internal static async Task<IReadOnlyList<EcomWaiting>> WaitingAsync(
    EcomDbContext db,
    EcomDoc doc,
    IReadOnlyList<EcomDocLink> entered,
    CancellationToken cancellationToken
  )
  {
    var purchased = entered.Select(link => Source(link).Id).Where(id => id != 0).Distinct().ToList();
    if (doc.WarehouseId is not { } warehouse || purchased.Count == 0)
    {
      return [];
    }

    var ids = await db.EcomDocLink
      .Where(link => link.TargetLineId != null && purchased.Contains(link.TargetLineId.Value) && link.SourceLineId != null)
      .Where(link => link.SourceDoc.DocTypeId == EcomData.DocType.CustomerOrderId)
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
      .. orders.Where(order => Holds(order, statuses[order.DocStatusId]))
        .Select(order => new EcomWaiting(order, EcomDocTotals.Tracked(db, order), statuses[order.DocStatusId]))
    ];
  }

  /// <summary>
  /// The reserve the waiting orders hold of the arrival of the supplier invoice <paramref name="doc"/>, by balance: for
  /// each product of the invoice's <paramref name="lines"/>, what the lines of <paramref name="waiting"/> hold of it, at
  /// most what the invoice brought, the quantities of its lines of the product. It is what the invoice may take although
  /// it is reserved when it leaves the posted statuses: those orders give it back in the same save. A product no waiting
  /// order holds is absent, and an invoice that names no warehouse is held nothing.
  /// </summary>
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

  /// <summary>A link of <paramref name="target"/> to <paramref name="source"/>: a stored end by its id, one not saved yet by its navigation.</summary>
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

  /// <summary>Whether the two links name one source document: a stored one by its id, one not saved yet by itself.</summary>
  private static bool SameSource(EcomDocLink one, EcomDocLink other) =>
    one.SourceDoc is not null && ReferenceEquals(one.SourceDoc, other.SourceDoc)
    || one.SourceDocId != 0 && one.SourceDocId == other.SourceDocId;

  /// <summary>
  /// Whether the link ties a line of a source to a line of a purchase order; it throws when the target document is not
  /// loaded.
  /// </summary>
  private static bool IsPurchaseRow(EcomDocLink link) =>
    (link.SourceLineId is not null || link.SourceLine is not null) && (link.TargetLineId is not null || link.TargetLine is not null)
    && TargetDoc(link).DocTypeId == EcomData.DocType.PurchaseOrderId;

  /// <summary>
  /// What the purchase line of the row asks and has not arrived: nothing once its order is neither New nor Sent, or the
  /// line is gone.
  /// </summary>
  private static decimal Asked(EcomDocLink link, IReadOnlyCollection<long>? gone)
  {
    var line = Target(link);
    return Asks(TargetDoc(link).DocStatusId) && !(line.Id != 0 && gone?.Contains(line.Id) == true) ? Math.Max(line.Qty - line.QtyDone, 0m) : 0m;
  }

  private static EcomDocLine Source(EcomDocLink link) =>
    link.SourceLine ?? throw new InvalidOperationException($"The source line {link.SourceLineId} of a link is not loaded.");

  private static EcomDocLine Target(EcomDocLink link) =>
    link.TargetLine ?? throw new InvalidOperationException($"The target line {link.TargetLineId} of a link is not loaded.");

  // The navigation reads null until the document is loaded, whatever its annotation says
  private static EcomDoc TargetDoc(EcomDocLink link) =>
    (EcomDoc?)link.TargetDoc ?? throw new InvalidOperationException($"The target document {link.TargetDocId} of a link is not loaded.");
}
