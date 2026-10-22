namespace DemoEcom.Migrations.Seeds;

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
    [EcomData.DocType.SupplierInvoiceId] =
    [
      EcomData.DocStatus.SupplierInvoiceDraftId, EcomData.DocStatus.SupplierInvoicePostedId, EcomData.DocStatus.SupplierInvoiceCanceledId
    ],
    [EcomData.DocType.StockIssueId] =
    [
      EcomData.DocStatus.StockIssueDraftId, EcomData.DocStatus.StockIssuePostedId, EcomData.DocStatus.StockIssueCanceledId
    ],
    [EcomData.DocType.StockTransferId] =
    [
      EcomData.DocStatus.StockTransferDraftId, EcomData.DocStatus.StockTransferPostedId, EcomData.DocStatus.StockTransferCanceledId
    ]
  };

  private static readonly int[] Places = [1, 1, 0, 1, 2, 1, 1, 0, 1, 1];

  /// <summary>The status of the next document of <paramref name="typeId"/>, dated <paramref name="age"/> days before the reference day.</summary>
  public long Next(long typeId, int age)
  {
    if (typeId != EcomData.DocType.PurchaseOrderId)
    {
      var turn = _taken[typeId] = _taken.GetValueOrDefault(typeId) + 1;
      return Stages[typeId][Places[(turn - 1) % Places.Length]];
    }

    if (age <= EcomSeedClock.RecentDays)
    {
      return _recent++ % 2 == 0 ? EcomData.DocStatus.PurchaseOrderSentId : EcomData.DocStatus.PurchaseOrderNewId;
    }

    return ++_older % CanceledEvery == 0 ? EcomData.DocStatus.PurchaseOrderCanceledId : EcomData.DocStatus.PurchaseOrderCompletedId;
  }
}
