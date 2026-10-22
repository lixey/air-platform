namespace Air.App.Form.Actions;

public sealed class ActionInputElement<TInput, TProp>(FormElementDto dto)
{
  public FormElementDto Dto => dto;

  public ActionInputElement<TInput, TProp> Label(string label)
  {
    dto.Label = label;
    return this;
  }

  public ActionInputElement<TInput, TProp> AsTextarea()
  {
    dto.Model = new FormTextareaModel();
    return this;
  }

  public ActionInputElement<TInput, TProp> AsDate()
  {
    dto.Type = UiFieldType.Date;
    dto.Model = new FormDateModel();
    return this;
  }

  public ActionInputElement<TInput, TProp> AsCheckbox()
  {
    dto.Type = UiFieldType.Checkbox;
    dto.Model = new FormCheckboxModel();
    return this;
  }

  public ActionInputElement<TInput, TProp> AsMoney(string? currency = null)
  {
    dto.Type = UiFieldType.Number;
    dto.Model = new FormMoneyModel { Currency = currency };
    return this;
  }

  public ActionInputElement<TInput, TProp> AsReference<TGridService>()
  {
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.Reference;
    dto.ReferenceServiceType = typeof(TGridService);
    dto.ReferencePath ??= dto.Path.EndsWith("Id", StringComparison.Ordinal) ? dto.Path[..^2] : dto.Path;
    dto.Model = new FormSelectModel
    {
      ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = ["name"]
    };
    return this;
  }

  public ActionInputElement<TInput, TProp> Where<TSource, TVal>(
    Expression<Func<TSource, TVal>> sourceMember,
    TVal value)
  {
    if (dto.Model is FormSelectModel selectModel)
    {
      var sourcePath = Air.App.Form.Elements.ToCamelPath(MemberPath.Of(sourceMember));
      var rule = FilterNode.Rule(sourcePath, FilterOperator.Equals, value);
      selectModel.ReferenceFilter = selectModel.ReferenceFilter is null
        ? rule
        : FilterNode.All(selectModel.ReferenceFilter, rule);
    }

    return this;
  }

  public ActionInputElement<TInput, TProp> Required(bool required = true)
  {
    dto.ExplicitRequired = required;
    dto.IsRequired = required;
    if (required)
    {
      dto.Constraints.AllowsNull = false;
      if (typeof(TProp) == typeof(string))
      {
        dto.Constraints.RejectsBlank = true;
      }
    }

    return this;
  }

  public ActionInputElement<TInput, TProp> Span(int span)
  {
    dto.Span = span;
    return this;
  }

  public ActionInputElement<TInput, TProp> Min(decimal min)
  {
    dto.Constraints.Min = JsonSerializer.SerializeToElement(min);
    return this;
  }

  public ActionInputElement<TInput, TProp> Max(decimal max)
  {
    dto.Constraints.Max = JsonSerializer.SerializeToElement(max);
    return this;
  }

  public ActionInputElement<TInput, TProp> Range(decimal min, decimal max)
  {
    dto.Constraints.Min = JsonSerializer.SerializeToElement(min);
    dto.Constraints.Max = JsonSerializer.SerializeToElement(max);
    return this;
  }

  public ActionInputElement<TInput, TProp> Rules(Action<ActionInputRuleBuilder<TProp>> configure)
  {
    var builder = new ActionInputRuleBuilder<TProp>(dto);
    configure(builder);
    return this;
  }
}
