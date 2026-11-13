namespace Platform.App.Form;

public sealed class FormRules<TEntity>
  where TEntity : class
{
  public List<Func<FormSaveContext<TEntity>, RuleContext<TEntity>, CancellationToken, Task>> Validators { get; } = [];

  public RuleBuilder<TEntity, TProp> For<TProp>(Expression<Func<TEntity, TProp>> member)
  {
    var compiled = member.Compile();
    string? path = null;
    var expr = member.Body is UnaryExpression ue ? ue.Operand : member.Body;
    if (expr is MemberExpression me)
    {
      path = char.ToLowerInvariant(me.Member.Name[0]) + me.Member.Name[1..];
    }

    var builder = new RuleBuilder<TEntity, TProp>(ctx => compiled(ctx.Entity), path);
    Validators.Add((ctx, rCtx, ct) => builder.ValidateAsync(ctx, rCtx, ct));
    return builder;
  }
}
