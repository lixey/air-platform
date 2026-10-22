namespace Air.Data.Querying;

/// <summary>
/// A queryable field: compiled server-side expression and allowed capabilities.
/// </summary>
public sealed class QueryField
{
  private readonly Type? _elementType;

  internal QueryField(
    string name,
    LambdaExpression expression,
    FieldKind kind,
    FieldCapabilities capabilities,
    IReadOnlySet<string> operators,
    bool isNullableInStore,
    Type? elementType = null)
  {
    Name = name;
    Expression = expression;
    Kind = kind;
    Capabilities = capabilities;
    Operators = operators;
    IsNullableInStore = isNullableInStore;
    _elementType = elementType;
  }

  public string Name { get; }
  public LambdaExpression Expression { get; }
  public Type ValueType => _elementType ?? Expression.ReturnType;
  public bool IsCollection => _elementType is not null;
  public FieldKind Kind { get; }
  public FieldCapabilities Capabilities { get; }
  public IReadOnlySet<string> Operators { get; }
  public bool IsNullableInStore { get; }

  public bool Can(FieldCapabilities capability) => (Capabilities & capability) == capability;

  internal QueryField With(FieldCapabilities capabilities) =>
    new(Name, Expression, Kind, capabilities, Operators, IsNullableInStore, _elementType);

  internal Expression Bind(ParameterExpression entity) => ParameterRebinder.Rebind(Expression, entity);
}
