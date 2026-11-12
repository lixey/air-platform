namespace DemoEcom.Migrations.Seeds.Internals;

internal sealed class EcomMoneySeed
{
  private const int MinCapitalSteps = 5;
  private const int MaxCapitalSteps = 20;
  private const decimal CapitalStep = 100_000m;

  private const string UkrainianLocale = "uk";
  private const string UkrainianCapitalNote = "\u041f\u043e\u0447\u0430\u0442\u043a\u043e\u0432\u0438\u0439 \u043a\u0430\u043f\u0456\u0442\u0430\u043b.";
  private const string EnglishCapitalNote = "Opening capital.";

  private static readonly EcomMoneyEnd[] Ends = [EcomMoneyEnd.Paid, EcomMoneyEnd.Paid, EcomMoneyEnd.Draft, EcomMoneyEnd.Paid, EcomMoneyEnd.Nothing, EcomMoneyEnd.Paid, EcomMoneyEnd.Paid, EcomMoneyEnd.Draft, EcomMoneyEnd.Paid, EcomMoneyEnd.Paid];

  private readonly EcomSeedBooks _books;
  private readonly Randomizer _random;
  private readonly EcomMoneyTurns _turns;
  private readonly long _tenantId;
  private readonly DateOnly _opening;
  private readonly IReadOnlyList<EcomMoneyOrganization> _organizations;
  private readonly IReadOnlyList<OrgMoneyAccount> _accounts;
  private readonly HashSet<long> _cash;
  private readonly string _capitalNote;

  private readonly Dictionary<string, decimal> _paid = [];

  private EcomMoneySeed(
    EcomSeedBooks books,
    Randomizer random,
    EcomMoneyTurns turns,
    long tenantId,
    DateOnly opening,
    IReadOnlyList<EcomMoneyOrganization> organizations,
    IReadOnlyList<OrgMoneyAccount> accounts,
    HashSet<long> cash,
    string capitalNote
  )
  {
    _books = books;
    _random = random;
    _turns = turns;
    _tenantId = tenantId;
    _opening = opening;
    _organizations = organizations;
    _accounts = accounts;
    _cash = cash;
    _capitalNote = capitalNote;
  }

  public static async Task<EcomMoneySeed> OfAsync(
    EcomDbContext db,
    Randomizer random,
    EcomMoneyTurns turns,
    AppSeedTenant tenant,
    EcomSeedBooks books,
    DateTimeOffset now,
    CancellationToken cancellationToken
  )
  {
    var tenantId = tenant.Row.Id;
    var organizations = await db.OrgOrganization.IgnoreQueryFilters()
      .Where(organization => organization.TenantId == tenantId && organization.IsActive).OrderBy(organization => organization.Id)
      .Select(organization => new EcomMoneyOrganization(organization.Id, organization.CurrencyId)).ToListAsync(cancellationToken);
    var accounts = await db.OrgMoneyAccount.IgnoreQueryFilters().AsNoTracking().Where(account => account.TenantId == tenantId)
      .OrderBy(account => account.Id).ToListAsync(cancellationToken);
    var cash = await db.OrgPaymentMethod.IgnoreQueryFilters().Where(method => method.TenantId == tenantId && method.IsCash)
      .Select(method => method.Id).ToListAsync(cancellationToken);
    return new EcomMoneySeed(
      books,
      random,
      turns,
      tenantId,
      EcomSeedClock.Today(books.Zone, now).AddDays(-EcomSeedClock.Days),
      organizations,
      accounts,
      [.. cash],
      tenant.Locale == UkrainianLocale ? UkrainianCapitalNote : EnglishCapitalNote
    );
  }

  public IEnumerable<EcomSeedStep> Opening()
  {
    foreach (var organization in _organizations)
    {
      if (EcomMoney.Account(_accounts, organization.Id, cash: false) is { } account)
      {
        yield return new EcomSeedStep(_opening, EcomSeedStep.Capital, () => [Capital(organization, account)]);
      }
    }
  }

  public IReadOnlyList<EcomSeedWritten> Paid(IReadOnlyList<EcomSeedWritten> written)
  {
    List<EcomSeedWritten> payments = [];
    foreach (var of in written)
    {
      payments.AddRange((of.Doc.DocTypeId, of.Doc.DocStatusId) switch
      {
        (EcomDocTypeData.CustomerOrderId, EcomDocStatusData.CustomerOrderCompletedId or EcomDocStatusData.CustomerOrderConfirmedId
          or EcomDocStatusData.CustomerOrderPausedId) => Received(of.Doc, written),
        (EcomDocTypeData.SupplierInvoiceId, EcomDocStatusData.SupplierInvoicePostedId) => Settled(of.Doc),
        (EcomDocTypeData.SalesReturnId, EcomDocStatusData.SalesReturnPostedId) => Refunded(of),
        _ => []
      });
    }

    return payments.Count == 0 ? written : [.. written, .. payments];
  }

  private EcomSeedWritten Capital(EcomMoneyOrganization organization, long accountId)
  {
    var doc = new EcomDoc
    {
      TenantId = _tenantId,
      OrganizationId = organization.Id,
      DocTypeId = EcomDocTypeData.IncomingPaymentId,
      Date = _opening,
      MoneyAccountId = accountId,
      CurrencyId = organization.CurrencyId,
      Amount = _random.Int(MinCapitalSteps, MaxCapitalSteps) * CapitalStep,
      Note = _capitalNote
    };
    return _books.Finish(doc, [], EcomDocStatusData.IncomingPaymentPostedId, _random);
  }

  private List<EcomSeedWritten> Received(EcomDoc order, IReadOnlyList<EcomSeedWritten> written)
  {
    var invoice = written.FirstOrDefault(of =>
      of.Doc.DocStatusId == EcomDocStatusData.SalesInvoiceDeliveredId && of.Links.Any(link => ReferenceEquals(link.SourceDoc, order)))?.Doc;
    var whole = EcomDocPayments.Open(order, 0m);
    var half = Math.Round(whole / 2m, 2, MidpointRounding.AwayFromZero);
    (EcomDoc Source, decimal Amount)[] parts = order.PaymentTermId switch
    {
      OrgPaymentTermData.FullPrepaymentId => [(order, whole)],
      OrgPaymentTermData.PartialPrepaymentId when invoice is null => [(order, half)],
      OrgPaymentTermData.PartialPrepaymentId => [(order, half), (invoice, EcomDocPayments.Open(invoice, half))],
      OrgPaymentTermData.PaymentOnDeliveryId when invoice is not null => [(invoice, EcomDocPayments.Open(invoice, 0m))],
      _ => []
    };
    parts = [.. parts.Where(part => part.Amount > 0m)];
    if (parts.Length == 0)
    {
      return [];
    }

    var end = Ends[_turns.NextOrder(order.PaymentTermId!.Value) % Ends.Length];
    if (end == EcomMoneyEnd.Nothing)
    {
      return [];
    }

    List<EcomSeedWritten> payments =
    [
      .. parts.Select((part, place) =>
        Enter(part.Source, EcomDocTypeData.IncomingPaymentId, part.Amount, posted: end == EcomMoneyEnd.Paid || place < parts.Length - 1))
    ];
    if (invoice is not null && order.PaidAmount + invoice.PaidAmount > 0m)
    {
      _paid[invoice.Number] = order.PaidAmount + invoice.PaidAmount;
    }

    return payments;
  }

  private List<EcomSeedWritten> Settled(EcomDoc invoice)
  {
    var amount = EcomDocPayments.Open(invoice, 0m);
    if (amount <= 0m)
    {
      return [];
    }

    var end = Ends[_turns.NextInvoice() % Ends.Length];
    return end == EcomMoneyEnd.Nothing ? [] : [Enter(invoice, EcomDocTypeData.OutgoingPaymentId, amount, posted: end == EcomMoneyEnd.Paid)];
  }

  private List<EcomSeedWritten> Refunded(EcomSeedWritten back)
  {
    if (back.Links.Select(link => (EcomDoc?)link.SourceDoc).FirstOrDefault(source => source is not null) is not { } invoice
        || !_paid.Remove(invoice.Number, out var paid))
    {
      return [];
    }

    var amount = Math.Min(EcomDocPayments.Open(back.Doc, 0m), paid);
    return amount <= 0m ? [] : [Enter(back.Doc, EcomDocTypeData.OutgoingPaymentId, amount, posted: true)];
  }

  private EcomSeedWritten Enter(EcomDoc source, long typeId, decimal amount, bool posted)
  {
    var incoming = typeId == EcomDocTypeData.IncomingPaymentId;
    var account = EcomMoney.Account(_accounts, source.OrganizationId, source.PaymentMethodId is { } method && _cash.Contains(method));
    var counts = posted && account is { } held && (incoming || _books.Holds(new EcomMoneyKey(source.OrganizationId, held)) >= amount);
    var statusId = (incoming, counts) switch
    {
      (true, true) => EcomDocStatusData.IncomingPaymentPostedId,
      (true, false) => EcomDocStatusData.IncomingPaymentDraftId,
      (false, true) => EcomDocStatusData.OutgoingPaymentPostedId,
      (false, false) => EcomDocStatusData.OutgoingPaymentDraftId
    };
    var entry = EcomDocPayments.Enter(source, typeId, statusId, source.Date, amount, account);
    var written = _books.Finish(entry.Doc, entry.Lines, statusId, _random, entry.Links);
    EcomDocPayments.Pay(source, entry.Links);
    return written;
  }
}
