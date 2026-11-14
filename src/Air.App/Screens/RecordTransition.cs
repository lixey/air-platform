namespace Platform.App.Screens;

public sealed class RecordTransition
{
  public required string Name { get; init; }
  public required IReadOnlyList<long> From { get; init; }
  public required long To { get; init; }
  public string? Label { get; init; }
  public string? Title { get; init; }
  public string? Confirm { get; init; }
  public RecordAsk? Ask { get; init; }
  public IReadOnlyList<RecordAvailability> Offers { get; init; } = [];
  public Right? RequiredRight { get; init; }
}
