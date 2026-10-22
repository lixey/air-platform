namespace Air.Data.Internal;

internal static class InferredFields
{
  public static IReadOnlyList<(string Name, LambdaExpression Expression)> FromSelector<TEntity, TRow>(
    Expression<Func<TEntity, TRow>> selector)
  {
    var fields = new List<(string Name, LambdaExpression Expression)>();
    Collect(selector.Body, string.Empty, selector.Parameters[0], fields);
    return fields;
  }

  public static IReadOnlyList<(string Name, LambdaExpression Expression)> FromEntity<TEntity>()
  {
    var entity = Expression.Parameter(typeof(TEntity), "e");
    return typeof(TEntity)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
      .Where(p => p.GetCustomAttribute<SensitiveAttribute>() is null && p.GetCustomAttribute<NotMappedAttribute>() is null)
      .Where(p => FieldKinds.Of(p.PropertyType) is not null)
      .Select(p => (p.Name, Expression.Lambda(Expression.Property(entity, p), entity)))
      .ToList();
  }

  private static void Collect(
    Expression node, string prefix, ParameterExpression entity, List<(string Name, LambdaExpression Expression)> fields)
  {
    foreach (var (member, value) in BindingsOf(Unwrap(node)))
    {
      var path = prefix + member.Name;
      var inner = Unwrap(value);
      if (FieldKinds.Of(value.Type) is not null)
      {
        fields.Add((path, Expression.Lambda(value, entity)));
      }
      else if (inner is MemberInitExpression or NewExpression { Arguments.Count: > 0 })
      {
        Collect(inner, path + ".", entity, fields);
      }
    }
  }

  private static IEnumerable<(MemberInfo Member, Expression Value)> BindingsOf(Expression node) => node switch
  {
    MemberInitExpression init => init.Bindings.OfType<MemberAssignment>().Select(b => (b.Member, b.Expression)),
    NewExpression created => BindingsOfNew(created),
    _ => throw new InvalidOperationException(
      $"A query map derived from a selector requires an object initializer (new Row {{ ... }}) or constructor call; '{node}' does not. Declare fields with Field().")
  };

  private static IEnumerable<(MemberInfo Member, Expression Value)> BindingsOfNew(NewExpression created)
  {
    if (created.Members is { } members)
    {
      return members.Zip(created.Arguments);
    }

    if (created.Constructor is not null)
    {
      var parameters = created.Constructor.GetParameters();
      if (parameters.Length == created.Arguments.Count)
      {
        return parameters.Zip(created.Arguments, (parameter, argument) => (PropertyOf(created.Type, parameter), argument));
      }
    }

    throw new InvalidOperationException(
      $"A query map derived from a selector requires an object construction; '{created}' does not expose members. Declare fields with Field().");
  }

  private static MemberInfo PropertyOf(Type row, ParameterInfo parameter) =>
    row.GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
    ?? throw new InvalidOperationException(
      $"A query map derived from a selector binds constructor parameters to properties of '{row.Name}'; its parameter '{parameter.Name}' names none. Rename it after a property or declare fields with Field().");

  private static Expression Unwrap(Expression node)
  {
    while (true)
    {
      switch (node)
      {
        case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast:
          node = cast.Operand;
          continue;
        case ConditionalExpression conditional when IsNull(conditional.IfTrue):
          node = conditional.IfFalse;
          continue;
        case ConditionalExpression conditional when IsNull(conditional.IfFalse):
          node = conditional.IfTrue;
          continue;
        default:
          return node;
      }
    }
  }

  private static bool IsNull(Expression node) =>
    node is ConstantExpression { Value: null } or DefaultExpression;
}
