namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceLine : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public long InvoiceId { get; set; }
  public string Title { get; set; } = string.Empty;
  public decimal Amount { get; set; }
}
