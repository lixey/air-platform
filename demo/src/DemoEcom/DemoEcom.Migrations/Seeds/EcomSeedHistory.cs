namespace DemoEcom.Migrations.Seeds;

internal static class EcomSeedHistory
{
  private static readonly Dictionary<long, EcomDocStatus> Statuses = EcomCodeRows.DocStatuses().ToDictionary(status => status.Id);

  private static readonly Dictionary<long, long> First =
    EcomCodeRows.DocStatuses().GroupBy(status => status.DocTypeId).ToDictionary(type => type.Key, type => type.First().Id);

  private static readonly Dictionary<long, string> Entering = new()
  {
    [EcomData.DocStatus.CustomerOrderInProgressId] = "Start",
    [EcomData.DocStatus.CustomerOrderConfirmedId] = "Confirm",
    [EcomData.DocStatus.CustomerOrderPausedId] = "Pause",
    [EcomData.DocStatus.CustomerOrderCompletedId] = "Complete",
    [EcomData.DocStatus.CustomerOrderCanceledId] = "Cancel",
    [EcomData.DocStatus.PurchaseOrderSentId] = "Send",
    [EcomData.DocStatus.PurchaseOrderCompletedId] = "Complete",
    [EcomData.DocStatus.PurchaseOrderCanceledId] = "Cancel",
    [EcomData.DocStatus.SalesInvoiceReadyToShipId] = "Ready",
    [EcomData.DocStatus.SalesInvoiceInTransitId] = "Ship",
    [EcomData.DocStatus.SalesInvoiceDeliveredId] = "Deliver",
    [EcomData.DocStatus.SalesInvoiceCanceledId] = "Cancel",
    [EcomData.DocStatus.SalesReturnPostedId] = "Post",
    [EcomData.DocStatus.SalesReturnCanceledId] = "Cancel",
    [EcomData.DocStatus.SupplierInvoicePostedId] = "Post",
    [EcomData.DocStatus.SupplierInvoiceCanceledId] = "Cancel",
    [EcomData.DocStatus.SupplierReturnPostedId] = "Post",
    [EcomData.DocStatus.SupplierReturnCanceledId] = "Cancel",
    [EcomData.DocStatus.IncomingPaymentPostedId] = "Post",
    [EcomData.DocStatus.IncomingPaymentCanceledId] = "Cancel",
    [EcomData.DocStatus.OutgoingPaymentPostedId] = "Post",
    [EcomData.DocStatus.OutgoingPaymentCanceledId] = "Cancel",
    [EcomData.DocStatus.StockTransferPostedId] = "Post",
    [EcomData.DocStatus.StockTransferCanceledId] = "Cancel",
    [EcomData.DocStatus.StockReceiptPostedId] = "Post",
    [EcomData.DocStatus.StockReceiptCanceledId] = "Cancel",
    [EcomData.DocStatus.StockIssuePostedId] = "Post",
    [EcomData.DocStatus.StockIssueCanceledId] = "Cancel"
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
