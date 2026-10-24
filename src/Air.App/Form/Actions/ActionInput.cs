namespace Platform.App.Form.Actions;

public sealed class ActionInput<TInput> where TInput : class
{
  private readonly List<FormElementDto> _elements = [];

  public IReadOnlyList<FormElementDto> Elements => _elements;

  public ActionInputElement<TInput, TProp> Add<TProp>(Expression<Func<TInput, TProp>> member)
  {
    ArgumentNullException.ThrowIfNull(member);
    var propName = "field";
    if (member.Body is MemberExpression me && me.Member is PropertyInfo pi)
    {
      propName = char.ToLowerInvariant(pi.Name[0]) + pi.Name[1..];
    }

    var label = Platform.App.Form.Elements.PathToLabel(propName);
    var fieldType = Platform.App.Form.Elements.ResolveFieldType(typeof(TProp));
    var temporalType = Platform.App.Form.Elements.ResolveTemporalType(typeof(TProp));
    var dto = new FormElementDto
    {
      Path = propName,
      Label = label,
      Type = fieldType,
      TemporalType = temporalType,
      Model = Platform.App.Form.Elements.ResolveDefaultModel(typeof(TProp)),
      Span = 12
    };

    _elements.Add(dto);
    return new ActionInputElement<TInput, TProp>(dto);
  }
}
