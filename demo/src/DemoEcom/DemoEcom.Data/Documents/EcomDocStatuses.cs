namespace DemoEcom.Data.Documents;

/// <summary>
/// The statuses of a document: which ones its type has, and the change itself, the posting. Which status may follow
/// which is not checked here: the status flow of the type owns it. A document that others are entered on does not leave
/// the posted statuses, unless they are purchase orders, which hold nothing. A status moves the stock of a document with
/// lines and the money of a payment.
/// </summary>
public static class EcomDocStatuses
{
  /// <summary>The statuses of the type in the order a document passes them, not tracked.</summary>
  public static async Task<IReadOnlyList<EcomDocStatus>> OfTypeAsync(EcomDbContext db, long docTypeId, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    return await db.EcomDocStatus.AsNoTracking().Where(status => status.DocTypeId == docTypeId).OrderBy(status => status.SortOrder)
      .ThenBy(status => status.Id).ToListAsync(cancellationToken);
  }

  /// <summary>
  /// Moves <paramref name="doc"/> from <paramref name="from"/> to <paramref name="to"/>, or returns why not and changes
  /// nothing. The caller owns the rule of what may follow what, the status flow of the document's type on a screen: the
  /// change checks no transition, and it assigns the status a move has already written, which changes nothing there. It
  /// refuses with <see cref="EcomDocRefusal.Followed"/> when the document would leave the posted statuses
  /// while a document entered on it is not canceled, <see cref="EcomDocRefusal.NoLines"/> when the status is posted and
  /// the document has no line, stored or tracked, <see cref="EcomDocRefusal.OverSource"/> when an entered document would
  /// enter the posted statuses with a line over what its source's line has open, and the refusals of
  /// <see cref="EcomStock"/> when the document names no warehouse for the stock the status moves, or the stock does not
  /// let the document's moves follow the status, and those of <see cref="EcomMoney"/> when a payment names no money
  /// account for a posted status, or an account would go below zero. Otherwise the stock is posted for the status, or the
  /// money of a payment, the status is set, <see cref="EcomDoc.PostedAt"/> is stamped with <paramref name="now"/> when
  /// the document enters the posted statuses, kept between two of them and cleared when the document leaves them, the
  /// totals of the lines are applied and the document's links follow them (<see cref="EcomDocEntering.Follow"/>).
  /// <para>
  /// A customer order sets its reserve before the posting writes it: entering a status that is posted and not final,
  /// Confirmed or Paused, its lines reserve the free stock of its warehouse through <see cref="EcomStock.ReserveAsync"/>,
  /// and entering any other status every line holds nothing. So the stock never refuses to confirm an order: what is
  /// short stays open on the line, its quantity less the done and the reserved, and a completed, a canceled and an order
  /// taken back release what they held.
  /// </para>
  /// <para>
  /// Followers hold their source: a document with a follower that is not canceled, a draft one too, moves between two
  /// posted statuses and never out of them, so what was entered on it stays true; once every follower is canceled it is
  /// free again. A purchase order is no such follower: a customer order leaves the posted statuses, canceled too, while
  /// a purchase order entered on its lines stands, and what that one brings arrives as free stock.
  /// </para>
  /// <para>
  /// A sales invoice and a sales return entered on another document count on its lines. Entering the posted statuses,
  /// the lines entered on a source line take together no more than it has open, its quantity less what is done; the
  /// reserve the source holds for the document is gathered, what the lines of a customer order in Confirmed or Paused of
  /// the document's own warehouse hold, at most what the document takes of them, and the document's own posting may take
  /// that reserve before the free stock; then, and only when the posting passed, the source lines count the quantity as
  /// done and hold it no more (<see cref="EcomDocEntering.Fulfil"/>), and each source order that holds a reserve is
  /// posted, which releases what shipped: the balances of the context are whole again before the caller's next document.
  /// Leaving the posted statuses, the document's own posting gives the stock back, or is refused when the goods of a
  /// return are gone, the source lines count the quantity as done no more, and a source order in Confirmed or Paused
  /// reserves again and is posted. Between two posted statuses no counter changes. A return counts on its invoice's
  /// lines alone, where the done quantity is the returned one, and never on the order's.
  /// </para>
  /// <para>
  /// A supplier invoice entered on a purchase order receives it. Entering Posted, its lines take together no more than
  /// their purchase lines have open; it posts itself, a receipt; the purchase lines count the quantity as done, the
  /// received one; a source purchase order in Sent is posted again, so it expects what it asks less what arrived; then
  /// its waiting orders (<see cref="EcomDocEntering.WaitingAsync"/>), the customer orders those purchase lines were made
  /// for, in Confirmed or Paused, of the invoice's organisation and warehouse, reserve and are posted, the oldest first,
  /// so the arrival is held for the lines that wait for it and what no waiting order takes stays free; and last the
  /// purchased quantities of those orders' lines are recomputed (<see cref="EcomDocEntering.PurchasedAsync"/>): what
  /// arrived is purchased no more. Leaving Posted, the reserve the waiting orders hold of its products, at most what it
  /// brought, is admitted to its own posting, so it is refused by the stock only when its goods left the warehouse or
  /// another document holds them; the purchase lines count the quantity as done no more and the purchase order is
  /// posted again, expecting it; then the waiting orders, the newest first, give back what the balances lack
  /// (<see cref="EcomStock.ReleaseAsync"/>) and are posted; and last the purchased quantities are recomputed. An invoice
  /// entered on nothing is a receipt alone.
  /// </para>
  /// <para>
  /// A supplier return entered on a supplier invoice counts on that invoice's lines, where the done quantity is the
  /// returned one: its lines take together no more than was received less what was returned, no reserve is held for it,
  /// so the stock refuses it when its goods are not free, and the purchase line is never touched. A purchase order
  /// counts on nothing: its links follow it, each document row with its source's part, and when it starts or stops
  /// asking its supplier, entering Completed or Canceled, the purchased quantities of the order lines its lines were
  /// entered on are recomputed, so a completed and a canceled order give back what did not arrive.
  /// </para>
  /// <para>
  /// A payment, incoming or outgoing, has no lines and moves money, not stock (<see cref="EcomDocTotals.HasLines"/>,
  /// <see cref="EcomMoney.Moves"/>): no line is read for it, a posted status asks none of it, and its amount stays the
  /// one its header holds. <see cref="EcomMoney.PostAsync"/> stands where the stock's posting does: Posted writes its
  /// move and its account's balance, Draft and Canceled take them back, and a status that would take an account below
  /// zero is refused, an outgoing payment over the account as an incoming one whose money is spent. A payment entered
  /// on a document is its follower, held and holding as every follower is: its document row follows its amount, and
  /// when it enters or leaves Posted, past the money's posting, the document it is entered on holds as paid what its
  /// posted payments paid, computed of their rows again (<see cref="EcomDocPayments.PayAsync"/>). Nothing of that
  /// document's lines, stock or status changes.
  /// </para>
  /// The document's lines are loaded once for all of it. It stages only: the caller saves, and a refusal stages nothing,
  /// on the document, on its sources, a paid amount among it, and on the orders that wait for it.
  /// </summary>
  public static async Task<IReadOnlyList<EcomDocRefusal>> ChangeAsync(
    EcomDbContext db,
    EcomDoc doc,
    EcomDocStatus from,
    EcomDocStatus to,
    DateTimeOffset now,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(from);
    ArgumentNullException.ThrowIfNull(to);
    var entering = !from.IsPosted && to.IsPosted;
    var leaving = from.IsPosted && !to.IsPosted;
    if (leaving && await EcomDocEntering.FollowedAsync(db, doc, cancellationToken))
    {
      return [new EcomDocRefusal(EcomDocRefusal.Followed)];
    }

    // A payment has no lines: none is read for it, and a posted status asks none of it
    var hasLines = EcomDocTotals.HasLines(doc.DocTypeId);
    var lines = hasLines ? await EcomDocTotals.LinesAsync(db, doc, cancellationToken) : [];
    if (hasLines && to.IsPosted && lines.Count == 0)
    {
      return [new EcomDocRefusal(EcomDocRefusal.NoLines)];
    }

    // What the document is entered on: its links, and the sources of its lines when the change counts on them
    var counts = EcomDocEntering.Fulfils(doc.DocTypeId);
    var purchase = doc.DocTypeId == EcomData.DocType.PurchaseOrderId;
    var payment = EcomMoney.Moves(doc.DocTypeId);
    var basis = counts || purchase || payment ? await EcomDocEntering.BasisAsync(db, doc, cancellationToken) : [];
    var entered = counts && (entering || leaving) ? EcomDocEntering.Entered(basis) : [];
    var sources = entered.Count > 0 ? await EcomDocEntering.SourcesAsync(db, entered, cancellationToken) : [];

    // A supplier invoice entered on a purchase order arrives for the customer orders that wait for it
    var arrival = doc.DocTypeId == EcomData.DocType.SupplierInvoiceId && entered.Count > 0;
    IReadOnlyList<EcomWaiting> waiting = [];
    Dictionary<EcomStockKey, decimal>? held = null;
    if (entering && entered.Count > 0)
    {
      var over = await EcomDocEntering.OverAsync(db, entered, cancellationToken);
      if (over.Count > 0)
      {
        return over;
      }

      held = EcomDocEntering.Held(doc, entered, sources);
    }
    else if (leaving && arrival)
    {
      // Before the posting: what those orders reserved of the arrival does not keep the invoice from being taken back
      waiting = await EcomDocEntering.WaitingAsync(db, doc, entered, cancellationToken);
      held = EcomDocEntering.Waited(doc, lines, waiting);
    }

    var reserved = lines.Select(line => line.QtyReserved).ToList();
    if (doc.DocTypeId == EcomData.DocType.CustomerOrderId)
    {
      if (to is { IsPosted: true, IsFinal: false })
      {
        await EcomStock.ReserveAsync(db, doc, lines, cancellationToken);
      }
      else
      {
        foreach (var line in lines)
        {
          line.QtyReserved = 0m;
        }
      }
    }

    // A type moves stock or money, never both
    var refusals = payment
      ? await EcomMoney.PostAsync(db, doc, to, cancellationToken)
      : await EcomStock.PostAsync(db, doc, lines, to, cancellationToken, held);
    if (refusals.Count > 0)
    {
      // A refused change leaves the lines as they were, the reserve it set too
      for (var n = 0; n < lines.Count; n++)
      {
        lines[n].QtyReserved = reserved[n];
      }

      return refusals;
    }

    doc.DocStatusId = to.Id;
    if (!to.IsPosted)
    {
      doc.PostedAt = null;
    }
    else if (!from.IsPosted)
    {
      doc.PostedAt = now;
    }

    EcomDocTotals.Apply(doc, lines);
    if (leaving)
    {
      // By the quantities the rows carried when the document entered the posted statuses
      EcomDocEntering.Fulfil(entered, false);
    }

    EcomDocEntering.Follow(doc, basis);
    if (entering)
    {
      EcomDocEntering.Fulfil(entered, true);
    }

    if (payment && (entering || leaving))
    {
      // Once the payment holds its status and its row its amount: the document it is entered on counts it, or no more
      await EcomDocPayments.PayAsync(db, basis, cancellationToken);
    }

    // Nothing is refused past this point: the reserve a source releases needs no stock, the one it takes again is at
    // most the free stock, which holds what the document just gave back, and what a purchase order expects is checked
    // by nothing
    foreach (var (source, status) in sources.Where(of => EcomDocEntering.Promises(of.Key, of.Value)).OrderBy(of => of.Key.Id))
    {
      var sourceLines = EcomDocTotals.Tracked(db, source);
      if (leaving && EcomDocEntering.Holds(source, status))
      {
        await EcomStock.ReserveAsync(db, source, sourceLines, cancellationToken);
      }

      await PostAgainAsync(db, source, sourceLines, status, doc, cancellationToken);
    }

    if (arrival)
    {
      if (entering)
      {
        // The oldest order first: each reserves what the ones before it left free of the arrival
        waiting = await EcomDocEntering.WaitingAsync(db, doc, entered, cancellationToken);
        foreach (var of in waiting)
        {
          await EcomStock.ReserveAsync(db, of.Order, of.Lines, cancellationToken);
          await PostAgainAsync(db, of.Order, of.Lines, of.Status, doc, cancellationToken);
        }
      }
      else
      {
        // The newest order first: each gives back what the balances still lack, at most what it holds, which together
        // is what the posting admitted
        foreach (var of in waiting.Reverse())
        {
          await EcomStock.ReleaseAsync(db, of.Order, of.Lines, cancellationToken);
          await PostAgainAsync(db, of.Order, of.Lines, of.Status, doc, cancellationToken);
        }
      }

      // What arrived is purchased no more, and what was taken back is purchased again
      await EcomDocEntering.PurchasedAsync(db, PurchaseLines(entered), [], cancellationToken);
    }

    if (purchase && EcomDocEntering.Asks(from.Id) != EcomDocEntering.Asks(to.Id))
    {
      // An order that asks no more gives back what did not arrive
      await EcomDocEntering.PurchasedAsync(db, [.. lines.Where(line => line.Id != 0).Select(line => line.Id)], [], cancellationToken);
    }

    return [];
  }

  /// <summary>
  /// Posts <paramref name="promising"/>, a source of <paramref name="doc"/> or an order that waits for it, over its lines
  /// as the context tracks them, so its promise rows follow the counters the change of <paramref name="doc"/> wrote. It
  /// throws when the stock refuses, which a release, a reserve taken of the free stock and an expected quantity never
  /// meet: past the document's own posting a refusal could no longer leave the context as it was. The refusal
  /// <see cref="EcomDocRefusal.ForeignWarehouse"/> is met here only for a document whose stored moves name a warehouse of
  /// another organisation, which is data written past the posting: by the code such a document's own change of status is
  /// refused first, so it holds no move.
  /// </summary>
  private static async Task PostAgainAsync(
    EcomDbContext db,
    EcomDoc promising,
    IReadOnlyList<EcomDocLine> lines,
    EcomDocStatus status,
    EcomDoc doc,
    CancellationToken cancellationToken
  )
  {
    if ((await EcomStock.PostAsync(db, promising, lines, status, cancellationToken)).Count > 0)
    {
      throw new InvalidOperationException($"The stock of the document '{promising.Number}' does not follow the document '{doc.Number}'.");
    }
  }

  /// <summary>The ids of the purchase lines the line rows <paramref name="entered"/> of a supplier invoice are entered on.</summary>
  private static List<long> PurchaseLines(IReadOnlyList<EcomDocLink> entered) =>
    [.. entered.Where(link => link.SourceLineId is not null).Select(link => link.SourceLineId!.Value).Distinct()];
}
