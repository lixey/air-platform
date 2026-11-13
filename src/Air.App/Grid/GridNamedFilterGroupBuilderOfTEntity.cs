namespace Platform.App.Grid;

public sealed class GridNamedFilterGroupBuilder<TEntity>(GridNamedFilterGroup<TEntity> group)
{
  public GridNamedFilterGroupBuilder<TEntity> Item(string key, string label, Expression<Func<TEntity, bool>> predicate)
  {
    group.Items.Add(new GridNamedFilterItem<TEntity>($"{group.Key}.{key}", label, predicate) { IsCountShown = group.IsCountShown });
    return this;
  }

  public GridNamedFilterGroupBuilder<TEntity> All(string label)
  {
    group.Items.Add(new GridNamedFilterItem<TEntity>($"{group.Key}.all", label, null) { IsCountShown = group.IsCountShown });
    return this;
  }

  public GridNamedFilterGroupBuilder<TEntity> Default()
  {
    if (group.Items.Count > 0)
    {
      group.Items[^1].IsDefault = true;
    }

    return this;
  }

  public GridNamedFilterGroupBuilder<TEntity> Color(string color)
  {
    if (group.Items.Count > 0)
    {
      group.Items[^1].Color = color;
    }

    return this;
  }

  public GridNamedFilterGroupBuilder<TEntity> Title(string title)
  {
    if (group.Items.Count > 0)
    {
      group.Items[^1].Title = title;
    }

    return this;
  }

  public GridNamedFilterGroupBuilder<TEntity> Title(string uiSource, string dbSource) =>
    Title(Feature.FormatSourceTitle(uiSource, dbSource));

  public GridNamedFilterGroupBuilder<TEntity> ShowCount(bool show = true)
  {
    if (group.Items.Count > 0)
    {
      group.Items[^1].IsCountShown = show;
    }
    else
    {
      group.IsCountShown = show;
    }

    return this;
  }

  public GridNamedFilterGroupBuilder<TEntity> ShowCounts(bool show = true)
  {
    group.IsCountShown = show;
    foreach (var item in group.Items)
    {
      item.IsCountShown = show;
    }

    return this;
  }

  public GridNamedFilterGroupBuilder<TEntity> Inline(bool inline = true)
  {
    group.IsInline = inline;
    return this;
  }

  public GridNamedFilterGroupBuilder<TEntity> Remember(bool remember = true)
  {
    group.IsRemembered = remember;
    return this;
  }
}
