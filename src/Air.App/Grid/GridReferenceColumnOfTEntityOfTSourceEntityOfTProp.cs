namespace Platform.App.Grid;

public class GridReferenceColumn<TEntity, TSourceEntity, TProp>(GridColumnDto dto, Columns owner)
  : GridColumn<TEntity, TProp>(dto, owner)
{
  public GridReferenceColumn<TEntity, TSourceEntity, TProp> Where<TVal>(
    Expression<Func<TSourceEntity, TVal>> sourceMember,
    TVal value)
  {
    base.Where(sourceMember, value);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> FilterBy<TFilterVal>(
    Expression<Func<TEntity, TFilterVal>> filterExpression)
  {
    Dto.FilterExpression = filterExpression;
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Hidden(bool hidden = true)
  {
    base.Hidden(hidden);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Required(bool required = true)
  {
    base.Required(required);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Label(string label)
  {
    base.Label(label);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Title(string title)
  {
    base.Title(title);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Title(string uiSource, string dbSource)
  {
    base.Title(uiSource, dbSource);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Width(int px)
  {
    base.Width(px);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Disabled(bool disabled = true)
  {
    base.Disabled(disabled);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Sortable(bool sortable = true)
  {
    base.Sortable(sortable);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Filterable(bool filterable = true)
  {
    base.Filterable(filterable);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Color(Expression<Func<TEntity, string?>> colorSelector)
  {
    base.Color(colorSelector);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Hue(Expression<Func<TEntity, string?>> hueSelector)
  {
    base.Color(hueSelector);
    return this;
  }
}
