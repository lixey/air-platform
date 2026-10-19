using System.Text.Json;

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
