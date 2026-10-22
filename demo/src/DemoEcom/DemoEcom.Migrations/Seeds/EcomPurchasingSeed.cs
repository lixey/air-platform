namespace DemoEcom.Migrations.Seeds;

internal sealed class EcomPurchasingSeed
{
  private const int ExtraEvery = 2;
  private const int MaxExtraQty = 5;
  private const int SortStep = 10;

  private const string UkrainianLocale = "uk";
  private const string UkrainianReturnNote = "\u0422\u043e\u0432\u0430\u0440 \u043f\u043e\u0437\u0430 \u0437\u0430\u043c\u043e\u0432\u043b\u0435\u043d\u043d\u044f\u043c: \u043f\u043e\u0432\u0435\u0440\u043d\u0435\u043d\u043e \u043f\u043e\u0441\u0442\u0430\u0447\u0430\u043b\u044c\u043d\u0438\u043a\u0443.";
  private const string EnglishReturnNote = "Goods beyond the order: returned to the supplier.";

  private static readonly End[] Ends = [End.Received, End.Sent, End.Nothing, End.New];

  private readonly EcomSeedBooks _books;
  private readonly Randomizer _random;
  private readonly EcomPurchasingTurns _turns;
  private readonly Dictionary<long, OrgProduct> _products;
  private readonly Dictionary<(long WarehouseId, long SupplierId), OrgProduct[]> _offers;
  private readonly string _returnNote;

  private EcomPurchasingSeed(
    EcomSeedBooks books,
    Randomizer random,
    EcomPurchasingTurns turns,
    Dictionary<long, OrgProduct> products,
    Dictionary<(long WarehouseId, long SupplierId), OrgProduct[]> offers,
    string returnNote
  )
  {
    _books = books;
    _random = random;
    _turns = turns;
    _products = products;
    _offers = offers;
    _returnNote = returnNote;
  }

  public static async Task<EcomPurchasingSeed> OfAsync(
    EcomDbContext db,
    EcomSeedVolume volume,
    int seed,
    Randomizer random,
    EcomPurchasingTurns turns,
    AppSeedTenant tenant,
    EcomSeedBooks books,
    CancellationToken cancellationToken
  )
  {
    var assortment = await EcomSeedAssortment.OfAsync(db, tenant.Row.Id, volume.OpeningLines, seed, cancellationToken);
    var traded = assortment.Values.SelectMany(products => products).Distinct().ToList();
    var products = await db.OrgProduct.IgnoreQueryFilters().AsNoTracking().Where(product => traded.Contains(product.Id))
      .ToDictionaryAsync(product => product.Id, cancellationToken);
    var offers = assortment
      .SelectMany(of => of.Value.Select(product => products[product]).GroupBy(product => (WarehouseId: of.Key, product.SupplierId)))
      .ToDictionary(of => of.Key, of => of.ToArray());
    return new EcomPurchasingSeed(books, random, turns, products, offers, tenant.Locale == UkrainianLocale ? UkrainianReturnNote : EnglishReturnNote);
  }

  public IReadOnlyList<EcomSeedWritten> Purchase(EcomDoc order, EcomDocLine[] lines)
  {
    List<EcomPurchase> wanted =
    [
      .. lines.Where(line => EcomDocEntering.ToPurchase(line) > 0m)
        .Select(line => new EcomPurchase(order, line, _products[line.ProductId], EcomDocEntering.ToPurchase(line)))
    ];
    if (wanted.Count == 0)
    {
      return [];
    }

    var end = Ends[_turns.NextShort() % Ends.Length];
    if (end == End.Nothing)
    {
      return [];
    }

    var purchases = EcomDocEntering.EnterPurchases(wanted, order.ManagerId, order.Date);
    var written = new List<EcomSeedWritten>();
    foreach (var purchase in purchases)
    {
      if (end == End.Received)
      {
        written.AddRange(Received(purchase));
        continue;
      }

      var statusId = end == End.Sent ? EcomData.DocStatus.PurchaseOrderSentId : EcomData.DocStatus.PurchaseOrderNewId;
      written.Add(_books.Finish(purchase.Doc, purchase.Lines, statusId, _random, purchase.Links));
    }

    if (end == End.Received)
    {
      EcomStock.Reserve(
        lines,
        lines.GroupBy(line => line.ProductId).ToDictionary(
          of => of.Key,
          of => _books.Free(new EcomStockKey(order.OrganizationId, order.WarehouseId!.Value, of.Key)) - of.Sum(line => line.QtyReserved)
        )
      );
    }

    EcomDocEntering.Purchased([.. purchases.SelectMany(purchase => purchase.Links)]);
    return written;
  }

  private List<EcomSeedWritten> Received(EcomDocEntry purchase)
  {
    var invoice = EcomDocEntering.Enter(
      purchase.Doc,
      purchase.Lines,
      line => line.Qty - line.QtyDone,
      EcomData.DocType.SupplierInvoiceId,
      EcomData.DocStatus.SupplierInvoicePostedId,
      purchase.Doc.Date
    ) ?? throw new InvalidOperationException("A purchase order made for an order line has nothing to receive.");
    List<EcomDocLine> lines = [.. invoice.Lines];
    EcomDocEntry? back = null;
    if (_turns.NextReceived() % ExtraEvery == 0)
    {
      var extra = Extra(invoice);
      lines.Add(extra);
      EcomDocTotals.Apply(invoice.Doc, lines);
      EcomDocEntering.Follow(invoice.Doc, invoice.Links);
      back = EcomDocEntering.Enter(
        invoice.Doc,
        [extra],
        line => line.Qty,
        EcomData.DocType.SupplierReturnId,
        EcomData.DocStatus.SupplierReturnPostedId,
        invoice.Doc.Date
      ) ?? throw new InvalidOperationException("The line a supplier invoice carries beyond its order has nothing to return.");
      back.Doc.Note = _returnNote;
      EcomDocEntering.Fulfil(back.Links, entering: true);
    }

    EcomDocEntering.Fulfil(invoice.Links, entering: true);
    List<EcomSeedWritten> written =
    [
      _books.Finish(purchase.Doc, purchase.Lines, EcomData.DocStatus.PurchaseOrderSentId, _random, purchase.Links),
      _books.Finish(invoice.Doc, lines, EcomData.DocStatus.SupplierInvoicePostedId, _random, invoice.Links)
    ];
    if (back is not null)
    {
      written.Add(_books.Finish(back.Doc, back.Lines, EcomData.DocStatus.SupplierReturnPostedId, _random, back.Links));
    }

    return written;
  }

  private EcomDocLine Extra(EcomDocEntry invoice)
  {
    var doc = invoice.Doc;
    var offer = _offers[(doc.WarehouseId!.Value, doc.CounterpartyId!.Value)];
    var named = invoice.Lines.Select(line => line.ProductId).ToHashSet();
    var others = offer.Where(of => !named.Contains(of.Id)).ToArray();
    var product = _random.ArrayElement(others.Length > 0 ? others : offer);
    var qty = (decimal)_random.Int(1, MaxExtraQty);
    return new EcomDocLine
    {
      TenantId = doc.TenantId,
      Doc = doc,
      SortOrder = invoice.Lines.Max(line => line.SortOrder) + SortStep,
      ProductId = product.Id,
      Qty = qty,
      Price = product.PurchasePrice,
      Amount = EcomDocTotals.LineAmount(qty, product.PurchasePrice, 0m)
    };
  }

  private enum End
  {
    Received,
    Sent,
    Nothing,
    New
  }
}
