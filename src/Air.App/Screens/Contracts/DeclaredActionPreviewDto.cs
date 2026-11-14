namespace Platform.App.Screens.Contracts;

public sealed class DeclaredActionPreviewDto
{
  public required string Service { get; init; }
  public required string Handle { get; init; }
  public required string Method { get; init; }
  public required string Label { get; init; }
  public string? Color { get; init; }
  public required string SelectionMode { get; init; }
  public IReadOnlyList<long> TargetIds { get; init; } = [];
  public IReadOnlyList<int>? TargetVersions { get; init; }
  public IReadOnlyList<string> TargetCaptions { get; init; } = [];
  public IReadOnlyList<FormElementDto> AskElements { get; init; } = [];
  public Dictionary<string, object?> AskValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public List<MessageDto> Warnings { get; init; } = [];
}
