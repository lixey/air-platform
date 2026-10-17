using System.Linq.Expressions;
using System.Reflection;
using Air.App.Components;

namespace Air.App.Form.Actions;

public sealed class ActionInput<TInput> where TInput : class
{
  private readonly List<FormElementDto> _elements = [];

  public IReadOnlyList<FormElementDto> Elements => _elements;

  public ActionInputElement<TInput, TProp> Add<TProp>(Expression<Func<TInput, TProp>> member)
  {
    ArgumentNullException.ThrowIfNull(member);
    string propName = "field";
    if (member.Body is MemberExpression me && me.Member is PropertyInfo pi)
    {
      propName = char.ToLowerInvariant(pi.Name[0]) + pi.Name[1..];
    }

    var dto = new FormElementDto
    {
      Path = propName,
      Label = propName,
      Type = typeof(TProp) == typeof(bool) ? UiFieldType.Checkbox : UiFieldType.Text,
      Model = new FormInputModel(),
      Span = 12
    };

    _elements.Add(dto);
    return new ActionInputElement<TInput, TProp>(dto);
  }
}
