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

  public ActionInputElement<TInput, TProp> Required(bool required = true)
  {
    dto.IsRequired = required;
    return this;
  }

  public ActionInputElement<TInput, TProp> Span(int span)
  {
    dto.Span = span;
    return this;
  }

  public ActionInputElement<TInput, TProp> Rules(Action<ActionInputRuleBuilder<TProp>> configure)
  {
    var builder = new ActionInputRuleBuilder<TProp>(dto);
    configure(builder);
    return this;
  }
}
