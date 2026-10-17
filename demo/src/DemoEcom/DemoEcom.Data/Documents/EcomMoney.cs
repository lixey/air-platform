namespace DemoEcom.Data.Documents;

/// <summary>
/// The money register, the stock's twin: the move a payment holds in a status, the sums of moves, the account a payment
/// entered on a document takes, and the posting that makes the stored move and the balance follow a status. A payment's
/// type says the sign of its move and the status's flag says when: while the status is posted. Every amount is in the
/// currency of the payment's organisation, which is its accounts' too.
/// </summary>
public static class EcomMoney
{
  /// <summary>
  /// Whether a document of the type moves money: an incoming and an outgoing payment, and no other type. A type moves
  /// stock or money, never both.
  /// </summary>
  public static bool Moves(long docTypeId) => docTypeId is EcomData.DocType.IncomingPaymentId or EcomData.DocType.OutgoingPaymentId;

  /// <summary>
  /// The moves <paramref name="doc"/> holds in <paramref name="status"/>, not added to any context: while the status is
  /// posted an incoming payment adds its amount to its money account and an outgoing payment takes it, one move of the
  /// whole amount. A payment of nothing gives no move, and neither does any other type or any other status. The move
  /// carries the document's tenant, organisation and date, and the document itself beside its id, so a payment not
  /// saved yet is added with its move. It reads no database and throws when a posted payment names no money account.
  /// </summary>
  public static IReadOnlyList<EcomMoneyMove> MovesOf(EcomDoc doc, EcomDocStatus status)
  {
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(status);
    if (!Moves(doc.DocTypeId) || !status.IsPosted)
    {
      return [];
    }

    if (doc.MoneyAccountId is not { } account)
    {
      throw new InvalidOperationException($"The document '{doc.Number}' moves money and names no money account for it.");
    }

    if (doc.Amount == 0m)
    {
      return [];
    }

    return
    [
      new EcomMoneyMove
      {
        TenantId = doc.TenantId,
        Doc = doc,
        DocId = doc.Id,
        Date = doc.Date,
        OrganizationId = doc.OrganizationId,
        MoneyAccountId = account,
        Amount = doc.DocTypeId == EcomData.DocType.IncomingPaymentId ? doc.Amount : -doc.Amount
      }
    ];
  }

  /// <summary>
  /// The sums of <paramref name="moves"/> per organisation and money account: what the balances of those moves alone
  /// would hold. A key whose moves cancel out stays, with zero.
  /// </summary>
  public static Dictionary<EcomMoneyKey, decimal> Sum(IEnumerable<EcomMoneyMove> moves)
  {
    ArgumentNullException.ThrowIfNull(moves);
    var sums = new Dictionary<EcomMoneyKey, decimal>();
    foreach (var move in moves)
    {
      var key = new EcomMoneyKey(move.OrganizationId, move.MoneyAccountId);
      sums[key] = sums.GetValueOrDefault(key) + move.Amount;
    }

    return sums;
  }

  /// <summary>
  /// The money account a payment entered on a document takes, picked by its kind: of <paramref name="accounts"/>, the
  /// first active cash one of the organisation when <paramref name="cash"/>, the document's payment method is cash, and
  /// the first active one that is not cash for any other payment, one of a method that is not cash or of no method; the
  /// first by <see cref="OrgMoneyAccount.SortOrder"/> and then by the id. Nothing when the organisation has no active
  /// account of that kind: the payment is left without one, the user picks it on the form, and the posting refuses the
  /// payment until then. It takes no currency, an account being in its organisation's, and reads no database.
  /// </summary>
  public static long? Account(IEnumerable<OrgMoneyAccount> accounts, long organizationId, bool cash)
  {
    ArgumentNullException.ThrowIfNull(accounts);
    return accounts.Where(account => account.OrganizationId == organizationId && account.IsActive && account.IsCash == cash)
      .OrderBy(account => account.SortOrder).ThenBy(account => account.Id).Select(account => (long?)account.Id).FirstOrDefault();
  }

  /// <summary>
  /// Makes the stored move of <paramref name="doc"/> and the balance it sums into those of <paramref name="status"/>, or
  /// returns why not and stages nothing. A document of a type that moves no money is left alone, and nothing is read for
  /// it. A posted status of a payment that names no money account is refused first,
  /// <see cref="EcomDocRefusal.NoAccount"/>, before anything is read. When the move changes, one whose money account is
  /// not of the payment's organisation is refused next, <see cref="EcomDocRefusal.ForeignAccount"/>, before a balance is
  /// read: no key of the database compares the organisations, so the posting, the one writer of the register, does. A
  /// status that gives no move is not asked, so such a payment is still taken back. The document's moves, the stored ones
  /// and the ones this context staged, are replaced by <see cref="MovesOf"/>, and every balance takes the difference of the two sums; a
  /// balance not kept yet is added, and one that comes back to zero stays. Moves that are already those of the status
  /// are left as they are. Before anything is staged every balance that falls is checked: no account, a cash desk or one
  /// in a bank, goes below zero. One that would gives a refusal <see cref="EcomDocRefusal.MoneyShort"/> with the
  /// account's name and what it is short of, in the order of the accounts. A balance an earlier document staged in this
  /// context counts as staged, so the second payment of a batch sees what the first took. It stages only: the caller
  /// saves. The screens take the lock of the organisation's register before the change of status that calls this, so a
  /// second posting at once waits and is checked against what the first left; a posting saved outside that lock fails by
  /// the balance's version, or by the unique index of the balances when both added the first one.
  /// </summary>
  public static async Task<IReadOnlyList<EcomDocRefusal>> PostAsync(
    EcomDbContext db,
    EcomDoc doc,
    EcomDocStatus status,
    CancellationToken cancellationToken
  )
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(status);
    if (!Moves(doc.DocTypeId))
    {
      return [];
    }

    if (status.IsPosted && doc.MoneyAccountId is null)
    {
      return [new EcomDocRefusal(EcomDocRefusal.NoAccount)];
    }

    var old = await MovesAsync(db, doc, cancellationToken);
    var next = MovesOf(doc, status);
    if (Same(old, next))
    {
      return [];
    }

    if (await ForeignAsync(db, doc, next, cancellationToken))
    {
      return [new EcomDocRefusal(EcomDocRefusal.ForeignAccount)];
    }

    var delta = Sum(next);
    foreach (var (key, amount) in Sum(old))
    {
      delta[key] = delta.GetValueOrDefault(key) - amount;
    }

    // In key order, so every posting adds and updates the balances in one order
    var changed = delta.Where(of => of.Value != 0m).OrderBy(of => of.Key.OrganizationId).ThenBy(of => of.Key.MoneyAccountId).ToList();
    var balances = await BalancesAsync(db, changed.Select(of => of.Key), cancellationToken);

    // What each account lacks, checked before anything is staged: a refused posting leaves the context as it was
    var lacking = changed.Where(of => of.Value < 0m)
      .Select(of => (of.Key.MoneyAccountId, Short: -((balances.TryGetValue(of.Key, out var kept) ? kept.Amount : 0m) + of.Value)))
      .Where(of => of.Short > 0m).ToList();
    if (lacking.Count > 0)
    {
      return await RefusalsAsync(db, lacking, cancellationToken);
    }

    db.EcomMoneyMove.RemoveRange(old);
    db.EcomMoneyMove.AddRange(next);
    foreach (var (key, amount) in changed)
    {
      if (!balances.TryGetValue(key, out var balance))
      {
        balance = new EcomMoneyBalance { TenantId = doc.TenantId, OrganizationId = key.OrganizationId, MoneyAccountId = key.MoneyAccountId };
        db.EcomMoneyBalance.Add(balance);
      }

      balance.Amount += amount;
    }

    return [];
  }

  /// <summary>
  /// The moves of <paramref name="doc"/> as this context's save would leave them: the stored ones are loaded into the
  /// context, then the tracked ones are read without those marked deleted, so moves staged and not saved yet count.
  /// </summary>
  private static async Task<List<EcomMoneyMove>> MovesAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken)
  {
    if (doc.Id != 0 && db.Entry(doc).State != EntityState.Added)
    {
      await db.EcomMoneyMove.Where(move => move.DocId == doc.Id).LoadAsync(cancellationToken);
    }

    return [.. db.EcomMoneyMove.Local.Where(move => move.Doc == doc || doc.Id != 0 && move.DocId == doc.Id)];
  }

  /// <summary>
  /// The balances of <paramref name="keys"/> that exist, tracked: the stored ones read in one query, and those this
  /// context added and has not saved yet, which no query returns.
  /// </summary>
  private static async Task<Dictionary<EcomMoneyKey, EcomMoneyBalance>> BalancesAsync(
    EcomDbContext db,
    IEnumerable<EcomMoneyKey> keys,
    CancellationToken cancellationToken
  )
  {
    var wanted = keys.ToHashSet();
    if (wanted.Count == 0)
    {
      return [];
    }

    var organizations = wanted.Select(key => key.OrganizationId).Distinct().ToList();
    var accounts = wanted.Select(key => key.MoneyAccountId).Distinct().ToList();
    await db.EcomMoneyBalance.Where(balance => organizations.Contains(balance.OrganizationId) && accounts.Contains(balance.MoneyAccountId))
      .LoadAsync(cancellationToken);
    return db.EcomMoneyBalance.Local.Select(balance => (Key: new EcomMoneyKey(balance.OrganizationId, balance.MoneyAccountId), balance))
      .Where(of => wanted.Contains(of.Key)).ToDictionary(of => of.Key, of => of.balance);
  }

  /// <summary>A refusal per account of <paramref name="lacking"/>, in their order, with the account's name and what it is short of.</summary>
  private static async Task<IReadOnlyList<EcomDocRefusal>> RefusalsAsync(
    EcomDbContext db,
    List<(long MoneyAccountId, decimal Short)> lacking,
    CancellationToken cancellationToken
  )
  {
    var ids = lacking.Select(of => of.MoneyAccountId).ToList();
    var names = await db.OrgMoneyAccount.AsNoTracking().Where(account => ids.Contains(account.Id))
      .ToDictionaryAsync(account => account.Id, account => account.Name, cancellationToken);
    return
    [
      .. lacking.Select(of => new EcomDocRefusal(EcomDocRefusal.MoneyShort, Short: of.Short, Account: names.GetValueOrDefault(of.MoneyAccountId)))
    ];
  }

  /// <summary>
  /// Whether a money account of <paramref name="next"/>, the moves the new status gives, is not one of the organisation of
  /// <paramref name="doc"/>: one read counts the named accounts that are, so an account no row answers, one of another
  /// tenant among them, is foreign too. No move asks nothing and reads nothing.
  /// </summary>
  private static async Task<bool> ForeignAsync(
    EcomDbContext db,
    EcomDoc doc,
    IReadOnlyList<EcomMoneyMove> next,
    CancellationToken cancellationToken
  )
  {
    var accounts = next.Select(move => move.MoneyAccountId).Distinct().ToList();
    if (accounts.Count == 0)
    {
      return false;
    }

    var own = await db.OrgMoneyAccount.CountAsync(
      account => accounts.Contains(account.Id) && account.OrganizationId == doc.OrganizationId, cancellationToken);
    return own != accounts.Count;
  }

  /// <summary>Whether the two lists hold the same rows, in any order: the date, the place and the amount of each.</summary>
  private static bool Same(List<EcomMoneyMove> old, IReadOnlyList<EcomMoneyMove> next) =>
    old.Count == next.Count && old.Select(Row).Order().SequenceEqual(next.Select(Row).Order());

  private static (DateOnly, long, long, decimal) Row(EcomMoneyMove move) => (move.Date, move.OrganizationId, move.MoneyAccountId, move.Amount);
}
