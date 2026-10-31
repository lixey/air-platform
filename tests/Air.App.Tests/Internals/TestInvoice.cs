namespace Platform.App.Tests.Internals;

internal sealed class TestInvoice : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public string Number { get; set; } = string.Empty;
  public long Status { get; set; } = 1;
  public bool IsHidden { get; set; }
  [KeepsVersion]
  public int PrintedCount { get; set; }
  [System.ComponentModel.DataAnnotations.Schema.NotMapped]
  public List<long> TagIds { get; set; } = [];
}
