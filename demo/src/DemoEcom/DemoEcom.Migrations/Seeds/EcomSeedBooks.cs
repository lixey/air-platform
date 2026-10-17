using Bogus;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;

namespace DemoEcom.Migrations.Seeds;

internal sealed class EcomSeedBooks(
  TimeZoneInfo zone,
  IReadOnlyDictionary<long, string> codes,
  IReadOnlyDictionary<EcomStockKey, decimal> demand
)
{
  private static readonly Dictionary<long, string> Prefixes = EcomCodeRows.DocTypes().ToDictionary(type => type.Id, type => type.Prefix);
  private static readonly Dictionary<long, EcomDocStatus> Statuses = EcomCodeRows.DocStatuses().ToDictionary(status => status.Id);

  private readonly Dictionary<EcomStockKey, EcomStockQty> _stock = [];
  private readonly Dictionary<EcomStockKey, decimal> _toShip = new(demand);
  private readonly Dictionary<long, List<long>> _held = [];
  private readonly Dictionary<EcomMoneyKey, decimal> _money = [];
  private readonly Dictionary<(long OrganizationId, long DocTypeId, int Year), int> _counters = [];

  public TimeZoneInfo Zone => zone;
  public IReadOnlyDictionary<EcomStockKey, EcomStockQty> Stock => _stock;
  public IReadOnlyDictionary<EcomMoneyKey, decimal> Money => _money;
  public IReadOnlyDictionary<(long OrganizationId, long DocTypeId, int Year), int> Counters => _counters;

  public decimal ToShip(EcomStockKey key) => _toShip.GetValueOrDefault(key);

  public decimal Free(EcomStockKey key) => Math.Max(_stock.GetValueOrDefault(key).Free - ToShip(key), 0m);

  public IReadOnlyList<long> Held(long warehouseId) => _held.TryGetValue(warehouseId, out var held) ? held : [];

  public decimal Holds(EcomMoneyKey key) => _money.GetValueOrDefault(key);

  public void Shipped(EcomStockKey key, decimal qty) => _toShip[key] = ToShip(key) - qty;

  public EcomSeedWritten Finish(
    EcomDoc doc,
    IReadOnlyList<EcomDocLine> lines,
    long statusId,
    Randomizer random,
    IReadOnlyList<EcomDocLink>? links = null
  )
  {
    var counter = (doc.OrganizationId, doc.DocTypeId, doc.Date.Year);
    var number = _counters[counter] = _counters.GetValueOrDefault(counter) + 1;
    doc.DocStatusId = statusId;
    doc.Number = EcomDocNumbers.Format(Prefixes[doc.DocTypeId], codes[doc.OrganizationId], doc.Date.Year, number);
    doc.PostedAt = Statuses[statusId].IsPosted ? EcomSeedClock.At(zone, doc.Date, random) : null;
    EcomDocTotals.Apply(doc, lines);
    return Post(doc, lines, links);
  }

  public EcomSeedWritten Post(EcomDoc doc, IReadOnlyList<EcomDocLine> lines, IReadOnlyList<EcomDocLink>? links = null)
  {
    var moves = EcomStock.MovesOf(doc, lines, Statuses[doc.DocStatusId]);
    var sums = EcomStock.Sum(moves);
    foreach (var move in moves)
    {
      var key = new EcomStockKey(move.OrganizationId, move.WarehouseId, move.ProductId);
      if (!sums.Remove(key, out var change))
      {
        continue;
      }

      if (!_stock.TryGetValue(key, out var balance))
      {
        if (!_held.TryGetValue(key.WarehouseId, out var held))
        {
          _held[key.WarehouseId] = held = [];
        }

        held.Add(key.ProductId);
      }

      _stock[key] = balance.Plus(change);
    }

    var money = EcomMoney.MovesOf(doc, Statuses[doc.DocStatusId]);
    foreach (var (key, amount) in EcomMoney.Sum(money))
    {
      _money[key] = _money.GetValueOrDefault(key) + amount;
    }

    return new EcomSeedWritten(doc, lines, moves, links ?? [], money, EcomSeedHistory.Of(doc, zone));
  }
}
