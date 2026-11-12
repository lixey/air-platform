namespace DemoEcom.Migrations.Seeds.Internals;

/// <summary>
/// The turns the statuses are dealt in, in the order the walk reaches the documents: per type for the supplier
/// invoices, the stock issues and the stock transfers, and for the purchase orders of the last week and the older ones.
/// </summary>
internal sealed class EcomStockTurns
{
  private const int CanceledEvery = 8;
  private readonly Dictionary<long, int> _taken = [];
  private int _recent;
  private int _older;

  private static readonly Dictionary<long, long[]> Stages = new()
  {
    [EcomDocTypeData.SupplierInvoiceId] =
    [
      EcomDocStatusData.SupplierInvoiceDraftId, EcomDocStatusData.SupplierInvoicePostedId, EcomDocStatusData.SupplierInvoiceCanceledId
    ],
    [EcomDocTypeData.StockIssueId] =
    [
      EcomDocStatusData.StockIssueDraftId, EcomDocStatusData.StockIssuePostedId, EcomDocStatusData.StockIssueCanceledId
    ],
    [EcomDocTypeData.StockTransferId] =
    [
      EcomDocStatusData.StockTransferDraftId, EcomDocStatusData.StockTransferPostedId, EcomDocStatusData.StockTransferCanceledId
    ]
  };

  private static readonly int[] Places = [1, 1, 0, 1, 2, 1, 1, 0, 1, 1];

  /// <summary>The status of the next document of <paramref name="typeId"/>, dated <paramref name="age"/> days before the reference day.</summary>
  public long Next(long typeId, int age)
  {
    if (typeId != EcomDocTypeData.PurchaseOrderId)
    {
      var turn = _taken[typeId] = _taken.GetValueOrDefault(typeId) + 1;
      return Stages[typeId][Places[(turn - 1) % Places.Length]];
    }

    if (age <= EcomSeedClock.RecentDays)
    {
      return _recent++ % 2 == 0 ? EcomDocStatusData.PurchaseOrderSentId : EcomDocStatusData.PurchaseOrderNewId;
    }

    return ++_older % CanceledEvery == 0 ? EcomDocStatusData.PurchaseOrderCanceledId : EcomDocStatusData.PurchaseOrderCompletedId;
  }
}
