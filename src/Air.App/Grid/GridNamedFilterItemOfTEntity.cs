namespace Platform.App.Grid;

public sealed class GridNamedFilterItem<TEntity>(string key, string label, Expression<Func<TEntity, bool>>? predicate)
{
  public string Key { get; } = key;
  public string Label { get; } = label;
  public Expression<Func<TEntity, bool>>? Predicate { get; } = predicate;
  public bool IsRemembered { get; set; }
  public bool IsDefault { get; set; }
  public bool IsCountShown { get; set; }
  public string? Color { get; set; }
  public string? Title { get; set; }
}
