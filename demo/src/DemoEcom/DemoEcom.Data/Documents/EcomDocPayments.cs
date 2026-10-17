namespace DemoEcom.Data.Documents;

/// <summary>
/// Payments entered on documents: an incoming payment on a customer order or a sales invoice, an outgoing payment on a
/// purchase order, a supplier invoice or, a refund, a sales return. A payment pays at most one document and has no line:
/// one link ties the two, a document row that carries the quantity 0 and the payment's amount, which follows the payment
/// as every document row follows its target (<see cref="EcomDocEntering.Follow"/>). What is open to pay on a document
/// counts every payment entered on it that is not canceled, a draft too, so a second payment does not pay twice over a
/// draft. What a document holds as paid, <see cref="EcomDoc.PaidAmount"/>, counts the posted ones alone: it is the sum of
/// the document rows whose target is a payment in Posted, computed of those rows again whenever a payment enters or
/// leaves Posted and never added to or taken from, so it cannot drift from its links. A document counts the payments
/// entered on it alone: an order and the invoice entered on it each count their own.
/// </summary>
public static class EcomDocPayments
{
  /// <summary>
  /// The payment of <paramref name="targetTypeId"/> entered on <paramref name="source"/>, in
  /// <paramref name="targetStatusId"/> and dated <paramref name="date"/>, of <paramref name="amount"/> on the money
  /// account <paramref name="accountId"/>, or on none, which the user names before the payment is posted; not added to
  /// any context and with no number yet. It takes the source's tenant, organisation, customer, contact, counterparty,
  /// payment method, currency and manager, as <see cref="EcomDocEntering.Enter"/> copies them; the source's currency is
  /// its organisation's, so the organisation is not read, and the rate is the model's 1. It has no line and no quantity.
  /// Its one link is the document row, with the quantity 0 and the amount. A stored source is named by its id and one
  /// that is not by its navigation, so the payment and its link are saved in one flush and a stored source is never
  /// added again. It reads no database and throws for a type that is no payment and for an amount that is not above
  /// zero: a payment of nothing is none.
  /// </summary>
  public static EcomDocEntry Enter(EcomDoc source, long targetTypeId, long targetStatusId, DateOnly date, decimal amount, long? accountId)
  {
    ArgumentNullException.ThrowIfNull(source);
    if (!EcomMoney.Moves(targetTypeId))
    {
      throw new ArgumentException($"The document type {targetTypeId} is no payment.", nameof(targetTypeId));
    }

    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
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
      MoneyAccountId = accountId,
      PaymentMethodId = source.PaymentMethodId,
      CurrencyId = source.CurrencyId,
      ManagerId = source.ManagerId,
      Amount = amount
    };
    return new EcomDocEntry(doc, [], [EcomDocEntering.Link(source, null, doc, null, 0m, amount)]);
  }

  /// <summary>
  /// What the payments entered on the documents <paramref name="sourceDocIds"/> already took, by document: the sum of
  /// the amounts of the document rows whose target is a payment that is not canceled, a draft counting as a posted one
  /// does. A document no such payment is entered on is absent. It reads the stored rows: a payment this context added or
  /// canceled and has not saved counts as stored.
  /// </summary>
  public static async Task<Dictionary<long, decimal>> TakenAsync(
    EcomDbContext db,
    IReadOnlyCollection<long> sourceDocIds,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(sourceDocIds);
    if (sourceDocIds.Count == 0)
    {
      return [];
    }

    // Summed here: SQLite sums no decimal
    var rows = await db.EcomDocLink.AsNoTracking().Where(
      link => link.SourceLineId == null
              && link.TargetLineId == null
              && sourceDocIds.Contains(link.SourceDocId)
              && (link.TargetDoc.DocTypeId == EcomData.DocType.IncomingPaymentId || link.TargetDoc.DocTypeId == EcomData.DocType.OutgoingPaymentId)
              && !link.TargetDoc.DocStatus.IsCanceled
    ).Select(link => new { link.SourceDocId, link.Amount }).ToListAsync(cancellationToken);
    return rows.GroupBy(row => row.SourceDocId).ToDictionary(of => of.Key, of => of.Sum(row => row.Amount ?? 0m));
  }

  /// <summary>
  /// What is open to pay on <paramref name="source"/>: its amount less what the payments not canceled
  /// <paramref name="taken"/> of it (<see cref="TakenAsync"/>), never less than nothing. A payment over the unpaid part
  /// is accepted, so what was taken may exceed the amount: nothing is open then.
  /// </summary>
  public static decimal Open(EcomDoc source, decimal taken)
  {
    ArgumentNullException.ThrowIfNull(source);
    return Math.Max(source.Amount - taken, 0m);
  }

  /// <summary>
  /// Whether <paramref name="payment"/> counts as paid on the document it is entered on: a payment, incoming or outgoing,
  /// in Posted. A draft and a canceled one count nowhere.
  /// </summary>
  public static bool Counts(EcomDoc payment)
  {
    ArgumentNullException.ThrowIfNull(payment);
    return payment.DocStatusId is EcomData.DocStatus.IncomingPaymentPostedId or EcomData.DocStatus.OutgoingPaymentPostedId;
  }

  /// <summary>
  /// Makes <paramref name="source"/> hold as paid what the posted payments entered on it paid: its
  /// <see cref="EcomDoc.PaidAmount"/> becomes the sum of the amounts of the document rows of <paramref name="links"/>
  /// whose source it is and whose target is a payment that counts (<see cref="Counts"/>), so nothing when there is none.
  /// The paid amount is computed over the rows given alone: the caller gives every payment's row of the document. The
  /// line rows, the rows of another source and the rows whose target is no payment are skipped. A source is matched as
  /// its rows name it, a stored one by its id and one not saved yet by itself. It changes the source alone, reads no
  /// database and throws when the target document of a document row of the source is not loaded.
  /// </summary>
  public static void Pay(EcomDoc source, IEnumerable<EcomDocLink> links)
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(links);
    source.PaidAmount = links.Where(EcomDocEntering.IsDocRow).Where(link => Of(link, source)).Select(link => (Payment: Target(link), link.Amount))
      .Where(of => Counts(of.Payment)).Sum(of => of.Amount ?? 0m);
  }

  /// <summary>
  /// <see cref="Pay"/> for the documents the payment whose links are <paramref name="basis"/> is entered on, over every
  /// payment of each: the stored sources are loaded into the context, tracked, with the document rows that tie them to
  /// payments and those payments, where what the context already tracks keeps its changes; then the tracked rows are
  /// read without those marked deleted. So a payment whose status this context changed, one whose amount it changed and
  /// a row it added and has not saved yet count as its save would leave them, and the second payment of a batch sees what
  /// the first paid. Called once the payment holds its new status and its row its amount. A payment entered on nothing
  /// changes nothing. It stages only: the caller saves.
  /// </summary>
  public static async Task PayAsync(EcomDbContext db, IReadOnlyList<EcomDocLink> basis, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(basis);
    var rows = basis.Where(EcomDocEntering.IsDocRow).ToList();
    if (rows.Count == 0)
    {
      return;
    }

    var stored = rows.Where(link => link.SourceDocId != 0).Select(link => link.SourceDocId).Distinct().ToList();
    if (stored.Count > 0)
    {
      await db.EcomDoc.Where(doc => stored.Contains(doc.Id)).LoadAsync(cancellationToken);
      await db.EcomDocLink
        .Where(link => link.SourceLineId == null && link.TargetLineId == null && stored.Contains(link.SourceDocId))
        .Where(link => link.TargetDoc.DocTypeId == EcomData.DocType.IncomingPaymentId || link.TargetDoc.DocTypeId == EcomData.DocType.OutgoingPaymentId)
        .Include(link => link.TargetDoc).LoadAsync(cancellationToken);
    }

    // A row another read left in the context without its target document is none of a payment: those were just loaded
    var payments = db.EcomDocLink.Local.Where(link => (EcomDoc?)link.TargetDoc is { } target && EcomMoney.Moves(target.DocTypeId)).ToList();
    foreach (var source in rows.Select(Source).Distinct())
    {
      Pay(source, payments);
    }
  }

  /// <summary>Whether the link's source is <paramref name="source"/>: a stored one by its id, one not saved yet by itself.</summary>
  private static bool Of(EcomDocLink link, EcomDoc source) =>
    ReferenceEquals(link.SourceDoc, source) || source.Id != 0 && link.SourceDocId == source.Id;

  // The navigations read null until the documents are loaded, whatever their annotations say
  private static EcomDoc Source(EcomDocLink link) =>
    (EcomDoc?)link.SourceDoc ?? throw new InvalidOperationException($"The source document {link.SourceDocId} of a link is not loaded.");

  private static EcomDoc Target(EcomDocLink link) =>
    (EcomDoc?)link.TargetDoc ?? throw new InvalidOperationException($"The target document {link.TargetDocId} of a link is not loaded.");
}
