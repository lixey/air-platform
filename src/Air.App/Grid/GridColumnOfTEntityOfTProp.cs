using Platform.Data.Internals;

namespace Platform.App.Grid;

public class GridColumn<TEntity, TProp>(GridColumnDto dto, Columns owner) : GridColumn<TProp>(dto, owner)
{
  public new GridColumn<TEntity, TProp> Label(string label)
  {
    base.Label(label);
    return this;
  }
  public new GridColumn<TEntity, TProp> Title(string title)
  {
    base.Title(title);
    return this;
  }
  public new GridColumn<TEntity, TProp> Title(string legacyUi, string legacyDb)
  {
    base.Title(legacyUi, legacyDb);
    return this;
  }
  public new GridColumn<TEntity, TProp> Width(int px)
  {
    base.Width(px);
    return this;
  }
  public new GridColumn<TEntity, TProp> Width(string width)
  {
    base.Width(width);
    return this;
  }
  public new GridColumn<TEntity, TProp> MaxWidth(int px)
  {
    base.MaxWidth(px);
    return this;
  }
  public new GridColumn<TEntity, TProp> Sticky(bool sticky = true)
  {
    base.Sticky(sticky);
    return this;
  }
  public new GridColumn<TEntity, TProp> StickyRight(bool stickyRight = true)
  {
    base.StickyRight(stickyRight);
    return this;
  }
  public new GridColumn<TEntity, TProp> Wrap(bool wrap = true)
  {
    base.Wrap(wrap);
    return this;
  }
  public new GridColumn<TEntity, TProp> Sortable(bool sortable = true)
  {
    base.Sortable(sortable);
    return this;
  }
  public new GridColumn<TEntity, TProp> Filterable(bool filterable = true)
  {
    base.Filterable(filterable);
    return this;
  }
  public new GridColumn<TEntity, TProp> Computed()
  {
    base.Computed();
    return this;
  }
  public new GridColumn<TEntity, TProp> Hidden(bool hidden = true)
  {
    base.Hidden(hidden);
    return this;
  }
  public new GridColumn<TEntity, TProp> Disabled(bool disabled = true)
  {
    base.Disabled(disabled);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsOpenForm<TFormService>() where TFormService : class
  {
    base.AsOpenForm<TFormService>();
    return this;
  }
  public GridColumn<TEntity, TProp> AsOpenForm<TFormService>(Expression<Func<TEntity, object?>> keySelector) where TFormService : class
  {
    Dto.Cell = new GridCellOpenFormModel
    {
      KeyPath = Columns.ToCamelPath(MemberPath.Of(keySelector))
    };
    owner.FormServiceType = typeof(TFormService);
    return this;
  }
  public GridColumn<TEntity, TProp> OpenForm(Func<TEntity, Type?> resolver)
  {
    Dto.Cell = new GridCellOpenFormModel();
    Dto.Width ??= 80;
    Dto.IsSticky = true;
    Dto.FormServiceResolver = row => row is TEntity typed ? resolver(typed) : null;
    return this;
  }
  public new GridColumn<TEntity, TProp> AsCheckbox()
  {
    base.AsCheckbox();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsSelect(IEnumerable<OptionDto> options)
  {
    base.AsSelect(options);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsDate()
  {
    base.AsDate();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsDatetime()
  {
    base.AsDatetime();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsNumber()
  {
    base.AsNumber();
    return this;
  }
  public new GridColumn<TEntity, TProp> Decimals(int decimals)
  {
    base.Decimals(decimals);
    return this;
  }
  public new GridColumn<TEntity, TProp> Grouped(bool isGrouped = true)
  {
    base.Grouped(isGrouped);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsMoney(string? currency = null)
  {
    base.AsMoney(currency);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsPercent()
  {
    base.AsPercent();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsPhone()
  {
    base.AsPhone();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsTel()
  {
    base.AsTel();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsEmail()
  {
    base.AsEmail();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsUrl()
  {
    base.AsUrl();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsWebsite()
  {
    base.AsWebsite();
    return this;
  }
  public new GridColumn<TEntity, TProp> Sum()
  {
    base.Sum();
    return this;
  }
  public new GridColumn<TEntity, TProp> Sum(Func<decimal, string?> colorSelector)
  {
    base.Sum(colorSelector);
    return this;
  }
  public GridColumn<TEntity, TProp> Sum(Expression<Func<TEntity, bool>> predicate, Func<decimal, bool, string?>? colorSelector = null)
  {
    Dto.IsSummed = true;
    Dto.SumPredicate = predicate;
    if (colorSelector != null)
    {
      Dto.SumColorEvaluator = colorSelector;
      ApplyColorPath();
    }

    return this;
  }
  public new GridColumn<TEntity, TProp> Stepper(bool hasStepper = true)
  {
    base.Stepper(hasStepper);
    return this;
  }
  public new GridColumn<TEntity, TProp> Step(double step)
  {
    base.Step(step);
    return this;
  }
  public new GridColumn<TEntity, TProp> Min(double min)
  {
    base.Min(min);
    return this;
  }
  public new GridColumn<TEntity, TProp> Max(double max)
  {
    base.Max(max);
    return this;
  }
  public new GridColumn<TEntity, TProp> Editable(bool isEditable = true)
  {
    base.Editable(isEditable);
    return this;
  }
  public new GridColumn<TEntity, TProp> Editable(Func<bool> condition)
  {
    base.Editable(condition);
    return this;
  }
  public GridColumn<TEntity, TProp> EditableWhen(Expression<Func<TEntity, bool>> predicate)
  {
    ApplyEditableWhen(predicate);
    return this;
  }
  public new GridColumn<TEntity, TProp> PickedBy<TSource>(Delegate method, int minLength = 2)
  {
    base.PickedBy<TSource>(method, minLength);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsCover<TFileGrid>() where TFileGrid : class
  {
    base.AsCover<TFileGrid>();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsImage()
  {
    base.AsImage();
    return this;
  }
  public new GridReferenceColumn<TEntity, TProp, TProp> AsReference<TGridService>()
  {
    base.AsReference<TGridService>();
    return new GridReferenceColumn<TEntity, TProp, TProp>(Dto, owner);
  }
  public new GridReferenceColumn<TEntity, TSource, TProp> AsReference<TGridService, TSource>(params Expression<Func<TSource, object?>>[] labelPaths)
  {
    base.AsReference<TGridService, TSource>(labelPaths);
    return new GridReferenceColumn<TEntity, TSource, TProp>(Dto, owner);
  }
  public new GridColumn<TEntity, TProp> Where<TSource, TVal>(Expression<Func<TSource, TVal>> sourceMember, TVal value)
  {
    base.Where(sourceMember, value);
    return this;
  }
  public GridColumn<TEntity, TProp> FilterBy<TFilterVal>(Expression<Func<TEntity, TFilterVal>> filterExpression)
  {
    Dto.FilterExpression = filterExpression;
    return this;
  }
  public GridColumn<TEntity, TProp> Color(Expression<Func<TEntity, string?>> colorSelector)
  {
    ApplyColorSelector(colorSelector);
    return this;
  }

  public GridColumn<TEntity, TProp> Hue(Expression<Func<TEntity, string?>> hueSelector) => Color(hueSelector);
}
