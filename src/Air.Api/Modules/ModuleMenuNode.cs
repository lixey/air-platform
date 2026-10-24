namespace Platform.Api.Modules;

public sealed class ModuleMenuNode
{
  public Feature? Feature { get; init; }
  public string? Handle { get; init; }
  public string? Title { get; init; }
  public string? Tooltip { get; init; }
  public string? Icon { get; init; }
  public bool IsHidden { get; init; }
  public bool IsSeparator { get; init; }
  public List<ModuleMenuNode> Children { get; init; } = [];
}
