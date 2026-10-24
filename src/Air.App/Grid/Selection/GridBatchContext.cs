namespace Platform.App.Grid.Selection;

public sealed class GridBatchContext<TEntity> : GridSelectionContext<TEntity> where TEntity : class, IEntity
{
  public GridBatchContext(
    IEnumerable<long> selectedIds,
    IEnumerable<TEntity> records,
    Func<CancellationToken, Task>? flusher = null,
    PlatformDbContext? db = null)
    : base(selectedIds, records, flusher, db)
  {
  }

  public string? OpenScreen { get; private set; }

  public long? OpenId { get; private set; }

  public void Open<TFormService>(long? id = null) where TFormService : class
  {
    OpenScreen = ServicePathResolver.Resolve(typeof(TFormService));
    OpenId = id;
  }
}
