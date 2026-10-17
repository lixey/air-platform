using Bogus;
using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;

namespace DemoEcom.Migrations.Seeds;

/// <summary>
/// The sales of Ecom's examples, as steps of the walk of the examples (<see cref="EcomSeedWalk"/>): what becomes of each
/// customer order on its date, by its status, through the functions the screens sell with. A Completed order gets a
/// Delivered sales invoice of every line (<see cref="EcomDocEntering.Enter"/>), which takes the goods from the order's
/// warehouse and counts on the order's lines (<see cref="EcomDocEntering.Fulfil"/>). A Confirmed or Paused order reserves
/// what its warehouse has free on that date (<see cref="EcomStock.Reserve"/>), in full or short, and by a draw gets no
/// invoice, a Draft or ReadyToShip one of every line, which moves nothing and leaves the order its reserve, or an
/// InTransit one of what the order reserved, which takes it from the warehouse and releases the reserve, as the change of
/// status does; what the reserve left short may be purchased in the same step (<see cref="EcomPurchasingSeed"/>), which
/// leaves the free stock as it found it. A New, InProgress or Canceled order is left as it is. Every thirtieth Completed
/// order of a tenant gets a Posted sales return of one line of its invoice, of a part or all of that line, three to
/// seven days later, so before the tenant's reference day: a step of its own on that date, which brings the goods back
/// to the warehouse, while the invoice's line counts them as returned from the start. An invoice and a return are
/// numbered and posted as the walk reaches them, in date order. The random generator is the sales' own, so the customer
/// orders and the stock documents draw nothing from it. No file is written.
/// </summary>
internal static class EcomSalesSeed
{
  /// <summary>The Completed order that gets a return: every thirtieth; and the days after the invoice the return is dated.</summary>
  private const int ReturnEvery = 30;

  private const int MinReturnDays = 3;

  /// <summary>
  /// Never more than <see cref="EcomSeedClock.RecentDays"/>: a Completed order is older than those days, so its return
  /// is dated before the tenant's reference day, as every document of the seed is.
  /// </summary>
  private const int MaxReturnDays = 7;

  /// <summary>No invoice: the first of what a Confirmed or Paused order may get.</summary>
  private const long NoInvoice = 0;

  /// <summary>What a Confirmed or Paused order gets: no invoice, or an invoice in one of these statuses; and how likely each is.</summary>
  private static readonly long[] Shipments =
  [
    NoInvoice, EcomData.DocStatus.SalesInvoiceDraftId, EcomData.DocStatus.SalesInvoiceReadyToShipId, EcomData.DocStatus.SalesInvoiceInTransitId
  ];

  private static readonly float[] ShipmentWeights = [0.4f, 0.15f, 0.15f, 0.3f];

  /// <summary>
  /// What the Completed orders among <paramref name="orders"/> ask of each warehouse and product: the demand the opening
  /// stock is sized to and the walk keeps until each of them ships.
  /// </summary>
  public static Dictionary<EcomStockKey, decimal> Demand(IReadOnlyList<(EcomDoc Doc, EcomDocLine[] Lines)> orders)
  {
    var demand = new Dictionary<EcomStockKey, decimal>();
    foreach (var (order, lines) in orders.Where(order => order.Doc.DocStatusId == EcomData.DocStatus.CustomerOrderCompletedId))
    {
      foreach (var line in lines)
      {
        var key = Key(order, line);
        demand[key] = demand.GetValueOrDefault(key) + line.Qty;
      }
    }

    return demand;
  }

  /// <summary>
  /// The steps of <paramref name="orders"/>, a tenant's customer orders in the order of their dates: each order on its
  /// date and, for every thirtieth Completed one, its return on a later date, drawn here. What an order gets is drawn
  /// when the walk reaches its step, from <paramref name="random"/>, the one generator of the sales of every tenant; a
  /// step leaves its documents through <paramref name="books"/>. With <paramref name="purchasing"/> the step of a
  /// Confirmed or Paused order also purchases what its reserve left short.
  /// </summary>
  public static IEnumerable<EcomSeedStep> Steps(
    EcomSeedBooks books,
    IReadOnlyList<(EcomDoc Doc, EcomDocLine[] Lines)> orders,
    Randomizer random,
    EcomPurchasingSeed? purchasing
  )
  {
    var returns = new Dictionary<EcomDoc, EcomDocEntry>();
    var completed = 0;
    foreach (var (order, lines) in orders)
    {
      DateOnly? returned = order.DocStatusId == EcomData.DocStatus.CustomerOrderCompletedId && ++completed % ReturnEvery == 0
        ? order.Date.AddDays(random.Int(MinReturnDays, MaxReturnDays))
        : null;
      yield return new EcomSeedStep(order.Date, EcomSeedStep.Order, () => order.DocStatusId switch
      {
        EcomData.DocStatus.CustomerOrderCompletedId => Shipped(books, order, lines, random, returned, returns),
        EcomData.DocStatus.CustomerOrderConfirmedId or EcomData.DocStatus.CustomerOrderPausedId => Held(books, order, lines, random, purchasing),
        _ => [books.Post(order, lines)]
      });
      if (returned is { } date)
      {
        yield return new EcomSeedStep(date, EcomSeedStep.Return, () => Returned(books, order, random, returns));
      }
    }
  }

  /// <summary>
  /// A Completed order with its Delivered invoice of every line, dated as the order: the invoice takes the goods from the
  /// warehouse and counts on the order's lines, and the demand it was kept for is asked no more. With
  /// <paramref name="returned"/> the return of that date is entered on the invoice and counted on its line here, before
  /// either is written, and kept in <paramref name="returns"/> for its own step.
  /// </summary>
  private static IReadOnlyList<EcomSeedWritten> Shipped(
    EcomSeedBooks books,
    EcomDoc order,
    EcomDocLine[] lines,
    Randomizer random,
    DateOnly? returned,
    Dictionary<EcomDoc, EcomDocEntry> returns
  )
  {
    if (EcomDocEntering.Enter(
          order,
          lines,
          line => line.Qty - line.QtyDone,
          EcomData.DocType.SalesInvoiceId,
          EcomData.DocStatus.SalesInvoiceDeliveredId,
          order.Date
        ) is not { } entry)
    {
      return [books.Post(order, lines)];
    }

    var invoice = books.Finish(entry.Doc, entry.Lines, EcomData.DocStatus.SalesInvoiceDeliveredId, random, entry.Links);
    EcomDocEntering.Fulfil(entry.Links, entering: true);
    foreach (var line in lines)
    {
      books.Shipped(Key(order, line), line.Qty);
    }

    if (returned is { } date)
    {
      // One line comes back, a part of it or all: whole units, as a customer returns goods
      var line = random.ArrayElement([.. entry.Lines]);
      var qty = (decimal)random.Int(1, (int)decimal.Floor(line.Qty));
      if (EcomDocEntering.Enter(entry.Doc, [line], _ => qty, EcomData.DocType.SalesReturnId, EcomData.DocStatus.SalesReturnPostedId, date) is { } back)
      {
        EcomDocEntering.Fulfil(back.Links, entering: true);
        returns[order] = back;
      }
    }

    return [books.Post(order, lines), invoice];
  }

  /// <summary>
  /// A Confirmed or Paused order with its reserve and what the draw gives it. The reserve is taken first, of what the
  /// warehouse has free on the order's date. An invoice in transit is entered on what the lines reserved, so it ships
  /// what the order holds and no more: it takes the goods, counts on the lines and takes their reserve, and the order
  /// then holds what the lines still reserve, nothing of what shipped. An order that reserved nothing gets no such
  /// invoice. A draft and an invoice ready to ship are entered on every line and move nothing. Then
  /// <paramref name="purchasing"/> purchases what the lines lack, and the order is posted last, with the reserve of an
  /// arrival among what its lines hold.
  /// </summary>
  private static IReadOnlyList<EcomSeedWritten> Held(
    EcomSeedBooks books,
    EcomDoc order,
    EcomDocLine[] lines,
    Randomizer random,
    EcomPurchasingSeed? purchasing
  )
  {
    EcomStock.Reserve(lines, lines.Select(line => line.ProductId).Distinct().ToDictionary(product => product, product => books.Free(Key(order, product))));
    var statusId = random.WeightedRandom(Shipments, ShipmentWeights);
    var shipped = statusId == EcomData.DocStatus.SalesInvoiceInTransitId;
    List<EcomSeedWritten> entered = [];
    if (statusId != NoInvoice
        && EcomDocEntering.Enter(
          order,
          lines,
          line => shipped ? line.QtyReserved : line.Qty - line.QtyDone,
          EcomData.DocType.SalesInvoiceId,
          statusId,
          order.Date
        ) is { } entry)
    {
      entered.Add(books.Finish(entry.Doc, entry.Lines, statusId, random, entry.Links));
      if (shipped)
      {
        EcomDocEntering.Fulfil(entry.Links, entering: true);
      }
    }

    entered.AddRange(purchasing?.Purchase(order, lines) ?? []);
    return [books.Post(order, lines), .. entered];
  }

  /// <summary>The return kept for <paramref name="order"/>, Posted on its date: its goods are back in the warehouse from then.</summary>
  private static IReadOnlyList<EcomSeedWritten> Returned(EcomSeedBooks books, EcomDoc order, Randomizer random, Dictionary<EcomDoc, EcomDocEntry> returns) =>
    returns.Remove(order, out var back) ? [books.Finish(back.Doc, back.Lines, EcomData.DocStatus.SalesReturnPostedId, random, back.Links)] : [];

  /// <summary>What a balance is kept of for a line of <paramref name="order"/>: its product in the order's warehouse.</summary>
  private static EcomStockKey Key(EcomDoc order, EcomDocLine line) => Key(order, line.ProductId);

  private static EcomStockKey Key(EcomDoc order, long productId) => new(order.OrganizationId, order.WarehouseId!.Value, productId);
}
