namespace Platform.App.Grid;

public sealed class GridNamedFilterGroup<TEntity>
{
  public GridNamedFilterGroup(string key, string? label)
  {
    CodeRows.EnsurePascalCaseHandle(key, "NamedFilter group");
    Key = key;
    Label = label;
  }

  public string Key { get; }
  public string? Label { get; }
  public bool IsRemembered { get; set; }
  public bool IsCountShown { get; set; }
  public bool IsInline { get; set; }
  public List<GridNamedFilterItem<TEntity>> Items { get; } = [];
}
