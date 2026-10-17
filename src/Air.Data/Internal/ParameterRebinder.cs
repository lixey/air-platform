namespace Air.Data.Internal;

internal sealed class ParameterRebinder(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
{
  public static Expression Rebind(LambdaExpression lambda, ParameterExpression target) =>
    new ParameterRebinder(lambda.Parameters[0], target).Visit(lambda.Body);

  protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : node;
}
