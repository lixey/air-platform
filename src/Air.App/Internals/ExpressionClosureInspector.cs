namespace Platform.App.Internals;

internal static class ExpressionClosureInspector
{
  public static bool ContainsClosure(Expression? expr) =>
    expr switch
    {
      null or ParameterExpression => false,
      ConstantExpression c => c.Value != null &&
                              !c.Type.IsPrimitive &&
                              !c.Type.IsEnum &&
                              c.Type != typeof(string) &&
                              c.Type != typeof(decimal),
      MemberExpression m => IsCollectionNavigationType(m.Type) || ContainsClosure(m.Expression),
      UnaryExpression u => ContainsClosure(u.Operand),
      BinaryExpression b => ContainsClosure(b.Left) || ContainsClosure(b.Right),
      ConditionalExpression cond => ContainsClosure(cond.Test) || ContainsClosure(cond.IfTrue) || ContainsClosure(cond.IfFalse),
      MethodCallExpression call => call.Method.DeclaringType == typeof(Queryable) ||
                                   call.Method.DeclaringType == typeof(Enumerable) ||
                                   ContainsClosure(call.Object) ||
                                   call.Arguments.Any(ContainsClosure),
      _ => true
    };

  private static bool IsCollectionNavigationType(Type type) =>
    type != typeof(string) &&
    type != typeof(byte[]) &&
    typeof(IEnumerable).IsAssignableFrom(type);
}
