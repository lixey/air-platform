namespace Platform.App.Grid;

public sealed class GridNamedFilterItem<TEntity>
{
  public GridNamedFilterItem(string key, string label, Expression<Func<TEntity, bool>>? predicate)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    foreach (var segment in key.Split('.'))
    {
      CodeRows.EnsurePascalCaseHandle(segment, $"NamedFilter '{key}'");
    }

    Key = key;
    Label = label;
    Predicate = predicate;
  }

  public string Key { get; }
  public string Label { get; }
  public Expression<Func<TEntity, bool>>? Predicate { get; }
  public bool IsRemembered { get; set; }
  public bool IsDefault { get; set; }
  public bool IsCountShown { get; set; }
  public string? Color { get; set; }
  public string? Title { get; set; }
}
