namespace Platform.App.Grid;

public sealed class GridNamedFilterBuilder<TEntity>(GridNamedFilterItem<TEntity> item)
{
  public GridNamedFilterBuilder<TEntity> Remember(bool remember = true)
  {
    item.IsRemembered = remember;
    return this;
  }

  public GridNamedFilterBuilder<TEntity> ShowCount(bool show = true)
  {
    item.IsCountShown = show;
    return this;
  }

  public GridNamedFilterBuilder<TEntity> Title(string title)
  {
    item.Title = title;
    return this;
  }

  public GridNamedFilterBuilder<TEntity> Title(string uiSource, string dbSource) =>
    Title(Feature.FormatSourceTitle(uiSource, dbSource));
}
