namespace Air.Data.Internal;

internal sealed class StoreNullability : ExpressionVisitor
{
  private bool _walksNavigation;

  public static bool Of(LambdaExpression expression)
  {
    var type = expression.ReturnType;
    if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null)
    {
      return true;
    }

    var probe = new StoreNullability();
    probe.Visit(expression.Body);
    return probe._walksNavigation;
  }

  protected override Expression VisitMember(MemberExpression node)
  {
    if (node.Expression is MemberExpression { Type: { IsValueType: false } owner } && owner != typeof(string))
    {
      _walksNavigation = true;
    }

    return base.VisitMember(node);
  }
}
