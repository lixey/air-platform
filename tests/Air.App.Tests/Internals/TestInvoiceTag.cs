namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceTag : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public long InvoiceId { get; set; }
  public long TagId { get; set; }
}
