using Air.Data.Internal;

namespace Air.Data.Querying;

/// <summary>
/// A queryable field: compiled server-side expression and allowed capabilities.
/// </summary>
public sealed class QueryField
{
  internal QueryField(
    string name,
    LambdaExpression expression,
    FieldKind kind,
    FieldCapabilities capabilities,
    IReadOnlySet<string> operators,
    bool isNullableInStore)
  {
    Name = name;
    Expression = expression;
    Kind = kind;
    Capabilities = capabilities;
    Operators = operators;
    IsNullableInStore = isNullableInStore;
  }

  public string Name { get; }
  public LambdaExpression Expression { get; }
  public Type ValueType => Expression.ReturnType;
  public FieldKind Kind { get; }
  public FieldCapabilities Capabilities { get; }
  public IReadOnlySet<string> Operators { get; }
  public bool IsNullableInStore { get; }

  public bool Can(FieldCapabilities capability) => (Capabilities & capability) == capability;

  internal QueryField With(FieldCapabilities capabilities) =>
    new(Name, Expression, Kind, capabilities, Operators, IsNullableInStore);

  internal Expression Bind(ParameterExpression entity) => ParameterRebinder.Rebind(Expression, entity);
}
