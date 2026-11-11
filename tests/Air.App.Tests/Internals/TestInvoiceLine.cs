namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceLine : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public long InvoiceId { get; set; }
  public TestInvoice? Invoice { get; set; }
  public long? TagId { get; set; }
  public TestTag? Tag { get; set; }
  public string Title { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string? HiddenNote { get; set; }
  public int TierCode { get; set; } = 1;
  public TestPriority Priority { get; set; } = TestPriority.Normal;

  [System.ComponentModel.DataAnnotations.Schema.NotMapped]
  public TestTag? OrphanNav { get; set; }
}
