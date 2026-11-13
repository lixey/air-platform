namespace Platform.App.Grid;

public sealed class GridNamedFilterGroup<TEntity>(string key, string? label)
{
  public string Key { get; } = key;
  public string? Label { get; } = label;
  public bool IsRemembered { get; set; }
  public bool IsCountShown { get; set; }
  public bool IsInline { get; set; }
  public List<GridNamedFilterItem<TEntity>> Items { get; } = [];
}
