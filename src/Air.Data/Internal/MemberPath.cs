namespace Air.Data.Internal;

public static class MemberPath
{
  public static string Of(LambdaExpression lambda) =>
    TryOf(lambda) ?? throw new InvalidOperationException($"'{lambda}' is not a member path; declare the field with an explicit name.");

  public static string? TryOf(LambdaExpression lambda)
  {
    var segments = new Stack<string>();
    var node = Strip(lambda.Body);
    while (node is MemberExpression member)
    {
      segments.Push(member.Member.Name);
      node = Strip(member.Expression);
    }

    return node is ParameterExpression && segments.Count > 0 ? string.Join('.', segments) : null;
  }

  private static Expression? Strip(Expression? node)
  {
    while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast)
    {
      node = cast.Operand;
    }

    return node;
  }
}
