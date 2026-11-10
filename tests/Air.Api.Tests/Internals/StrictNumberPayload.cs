namespace Platform.Api.Tests.Internals;

internal sealed class StrictNumberPayload
{
  public int Count { get; set; }
  public int? OptionalCount { get; set; }
  public long Total { get; set; }
  public long? OptionalTotal { get; set; }
  public decimal Amount { get; set; }
  public decimal? OptionalAmount { get; set; }
  public DateTime CreatedOn { get; set; }
  public DateTime? OptionalCreatedOn { get; set; }
}
