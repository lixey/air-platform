namespace Platform.App.Grid;

public sealed class GridHooks<TEntity>(string? moduleHandle = null)
  where TEntity : class, IEntity
{
  internal bool IsMultiDeleteAllowed { get; private set; }
  internal List<Type> DependentEntityTypes { get; } = [];
  public List<Func<GridDeleteContext<TEntity>, CancellationToken, Task>> BeforeDeleteActions { get; } = [];
  public List<Func<GridDeleteContext<TEntity>, CancellationToken, Task>> AfterDeleteActions { get; } = [];

  public void AllowMultiDelete(bool allow = true) => IsMultiDeleteAllowed = allow;

  public void PreventDeleteWhenUsedBy<TDependent>(
    Expression<Func<TDependent, long>> foreignKey,
    string? message = null)
    where TDependent : class
  {
    ArgumentNullException.ThrowIfNull(foreignKey);
    DependentEntityTypes.Add(typeof(TDependent));
    BeforeDeleteActions.Add(async (c, ct) =>
    {
      if (c.Db is null || c.Records.IsEmpty)
      {
        return;
      }

      var ids = c.Records.Select(r => r.Id).Distinct().ToArray();
      var query = c.Db.Set<TDependent>()
        .AsNoTracking()
        .Select(foreignKey)
        .Where(fk => ids.Contains(fk))
        .Distinct();
      var usedIds = query is IAsyncEnumerable<long>
        ? await query.ToListAsync(ct)
        : query.ToList();
      RefuseUsedRecords(c, usedIds, message);
    });
  }

  public void PreventDeleteWhenUsedBy<TDependent>(
    Expression<Func<TDependent, long?>> foreignKey,
    string? message = null)
    where TDependent : class
  {
    ArgumentNullException.ThrowIfNull(foreignKey);
    DependentEntityTypes.Add(typeof(TDependent));
    BeforeDeleteActions.Add(async (c, ct) =>
    {
      if (c.Db is null || c.Records.IsEmpty)
      {
        return;
      }

      var ids = c.Records.Select(r => r.Id).Distinct().ToArray();
      var query = c.Db.Set<TDependent>()
        .AsNoTracking()
        .Select(foreignKey)
        .Where(fk => fk.HasValue && ids.Contains(fk.Value))
        .Select(fk => fk!.Value)
        .Distinct();
      var usedIds = query is IAsyncEnumerable<long>
        ? await query.ToListAsync(ct)
        : query.ToList();
      RefuseUsedRecords(c, usedIds, message);
    });
  }

  private void RefuseUsedRecords(GridDeleteContext<TEntity> context, List<long> usedIds, string? message)
  {
    if (usedIds.Count == 0)
    {
      return;
    }

    var loc = !string.IsNullOrEmpty(moduleHandle)
      ? Localizer.Instance.For(moduleHandle)
      : Localizer.Instance;
    var text = !string.IsNullOrEmpty(message) ? loc[message] : loc["DeleteRefusedInUse"];
    foreach (var usedId in usedIds)
    {
      if (!context.Errors.Any(e => e.RecordId == usedId && string.Equals(e.Title, text, StringComparison.Ordinal)))
      {
        context.Refuse(text, usedId);
      }
    }
  }

  public void BeforeDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDeleteActions.Add(handler);

  public void BeforeDelete(Action<GridDeleteContext<TEntity>> handler) =>
    BeforeDeleteActions.Add((c, _) =>
    {
      handler(c);
      return Task.CompletedTask;
    });

  public void AfterDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => AfterDeleteActions.Add(handler);

  public void AfterDelete(Action<GridDeleteContext<TEntity>> handler) =>
    AfterDeleteActions.Add((c, _) =>
    {
      handler(c);
      return Task.CompletedTask;
    });
}
