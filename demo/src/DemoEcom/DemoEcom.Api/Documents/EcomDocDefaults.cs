using DemoEcom.Data;
using DemoEcom.Data.Documents;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Documents;

public static class EcomDocDefaults
{
  public static Task NewAsync(EcomDoc doc, long typeId, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(doc);
    doc.DocTypeId = typeId;
    doc.Date = DateOnly.FromDateTime(DateTime.UtcNow);
    return Task.CompletedTask;
  }

  public static async Task FromOrganizationAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    var organization = doc.OrganizationId;
    doc.CurrencyId = await db.OrgOrganization.Where(row => row.Id == organization).Select(row => row.CurrencyId)
      .SingleAsync(cancellationToken);
  }

  public static async Task FromCustomerAsync(EcomDbContext db, EcomDoc doc, long? userId, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    doc.ManagerId ??= userId;

    if (doc.CustomerId is not { } customerId)
    {
      return;
    }

    var delivered = IsDelivered(doc.DocTypeId);
    if (!delivered && !EcomMoney.Moves(doc.DocTypeId))
    {
      return;
    }

    if (doc.PaymentMethodId is not null && (!delivered || doc is { DeliveryTypeId: not null, PaymentTermId: not null }))
    {
      return;
    }

    var customer = await db.OrgCustomer.Where(row => row.Id == customerId)
      .Select(row => new { row.DeliveryTypeId, row.PaymentMethodId, row.PaymentTermId }).FirstOrDefaultAsync(cancellationToken);
    doc.PaymentMethodId ??= customer?.PaymentMethodId;
    if (delivered)
    {
      doc.DeliveryTypeId ??= customer?.DeliveryTypeId;
      doc.PaymentTermId ??= customer?.PaymentTermId;
    }
  }

  private static bool IsDelivered(long typeId) => typeId is EcomData.DocType.CustomerOrderId or EcomData.DocType.SalesInvoiceId;
}
