namespace Air.App.Screens;

public sealed class RecordTransition
{
  public required string Name { get; init; }
  public required IReadOnlyList<long> From { get; init; }
  public required long To { get; init; }
  public string? Confirm { get; init; }
  public RecordAsk? Ask { get; init; }
  public IReadOnlyList<RecordAvailability> Offers { get; init; } = [];
}
