namespace DemoEcom.Migrations.Seeds.Internals;

internal static class EcomSeedHistory
{
  private static readonly Dictionary<long, EcomDocStatus> Statuses = EcomCodeRows.DocStatuses().ToDictionary(status => status.Id);

  private static readonly Dictionary<long, long> First =
    EcomCodeRows.DocStatuses().GroupBy(status => status.DocTypeId).ToDictionary(type => type.Key, type => type.First().Id);

  private static readonly Dictionary<long, string> Entering = new()
  {
    [EcomDocStatusData.CustomerOrderInProgressId] = "Start",
    [EcomDocStatusData.CustomerOrderConfirmedId] = "Confirm",
    [EcomDocStatusData.CustomerOrderPausedId] = "Pause",
    [EcomDocStatusData.CustomerOrderCompletedId] = "Complete",
    [EcomDocStatusData.CustomerOrderCanceledId] = "Cancel",
    [EcomDocStatusData.PurchaseOrderSentId] = "Send",
    [EcomDocStatusData.PurchaseOrderCompletedId] = "Complete",
    [EcomDocStatusData.PurchaseOrderCanceledId] = "Cancel",
    [EcomDocStatusData.SalesInvoiceReadyToShipId] = "Ready",
    [EcomDocStatusData.SalesInvoiceInTransitId] = "Ship",
    [EcomDocStatusData.SalesInvoiceDeliveredId] = "Deliver",
    [EcomDocStatusData.SalesInvoiceCanceledId] = "Cancel",
    [EcomDocStatusData.SalesReturnPostedId] = "Post",
    [EcomDocStatusData.SalesReturnCanceledId] = "Cancel",
    [EcomDocStatusData.SupplierInvoicePostedId] = "Post",
    [EcomDocStatusData.SupplierInvoiceCanceledId] = "Cancel",
    [EcomDocStatusData.SupplierReturnPostedId] = "Post",
    [EcomDocStatusData.SupplierReturnCanceledId] = "Cancel",
    [EcomDocStatusData.IncomingPaymentPostedId] = "Post",
    [EcomDocStatusData.IncomingPaymentCanceledId] = "Cancel",
    [EcomDocStatusData.OutgoingPaymentPostedId] = "Post",
    [EcomDocStatusData.OutgoingPaymentCanceledId] = "Cancel",
    [EcomDocStatusData.StockTransferPostedId] = "Post",
    [EcomDocStatusData.StockTransferCanceledId] = "Cancel",
    [EcomDocStatusData.StockReceiptPostedId] = "Post",
    [EcomDocStatusData.StockReceiptCanceledId] = "Cancel",
    [EcomDocStatusData.StockIssuePostedId] = "Post",
    [EcomDocStatusData.StockIssueCanceledId] = "Cancel"
  };

  public static IReadOnlyList<EcomDocMove> Of(EcomDoc doc, TimeZoneInfo zone)
  {
    ArgumentNullException.ThrowIfNull(doc);
    var first = First[doc.DocTypeId];
    if (doc.DocStatusId == first)
    {
      return [];
    }

    if (!Entering.TryGetValue(doc.DocStatusId, out var transition))
    {
      throw new InvalidOperationException(
        $"The seed assigns the status {Statuses[doc.DocStatusId].Handle} and names no transition that enters it.");
    }

    return
    [
      new EcomDocMove
      {
        TenantId = doc.TenantId,
        Doc = doc,
        Transition = transition,
        FromId = first,
        ToId = doc.DocStatusId,
        At = doc.PostedAt ?? EcomSeedClock.Opening(zone, doc.Date)
      }
    ];
  }
}
