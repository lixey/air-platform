namespace Air.Data.Internal;

internal sealed class SensitiveGuard : ExpressionVisitor
{
  private MemberInfo? _found;

  public static void Ensure(LambdaExpression expression, string field)
  {
    var guard = new SensitiveGuard();
    guard.Visit(expression.Body);
    if (guard._found is { } member)
    {
      throw new InvalidOperationException(
        $"Field '{field}' reads '{member.DeclaringType?.Name}.{member.Name}', which is marked [Sensitive] and can never be queried.");
    }
  }

  protected override Expression VisitMember(MemberExpression node)
  {
    if (node.Member.GetCustomAttribute<SensitiveAttribute>() is not null)
    {
      _found ??= node.Member;
    }

    return base.VisitMember(node);
  }
}
