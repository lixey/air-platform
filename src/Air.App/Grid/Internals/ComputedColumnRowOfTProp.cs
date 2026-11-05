namespace Platform.App.Grid.Internals;

internal sealed class ComputedColumnRow<TProp>
{
  public long Id { get; set; }
  public TProp? Value { get; set; }

  public static async Task<Dictionary<long, object?>> EvaluateAsync<TEntity>(
    IQueryable<TEntity> query,
    IReadOnlyList<long> rowIds,
    LambdaExpression valueExpression,
    Func<object?, object?>? formatter,
    CancellationToken ct)
    where TEntity : class, IEntity
  {
    var typedExpr = (Expression<Func<TEntity, TProp>>)valueExpression;
    var param = typedExpr.Parameters[0];
    var idProp = typeof(TEntity).GetProperty(nameof(IEntity.Id))!;
    var rowType = typeof(ComputedColumnRow<TProp>);
    var selector = Expression.Lambda<Func<TEntity, ComputedColumnRow<TProp>>>(
      Expression.MemberInit(
        Expression.New(rowType),
        Expression.Bind(rowType.GetProperty(nameof(Id))!, Expression.Property(param, idProp)),
        Expression.Bind(rowType.GetProperty(nameof(Value))!, typedExpr.Body)),
      param);

    var rows = await query
      .Where(e => rowIds.Contains(e.Id))
      .Select(selector)
      .ToListAsync(ct);

    var result = new Dictionary<long, object?>(rows.Count);
    foreach (var row in rows)
    {
      object? val = row.Value;
      if (formatter != null)
      {
        val = formatter(val);
      }

      result[row.Id] = val;
    }

    return result;
  }
}
