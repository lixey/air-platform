namespace Air.App.Grid.Selection;

public sealed class GridDeleteContext<TEntity> : GridSelectionContext<TEntity> where TEntity : class
{
  public GridDeleteContext(
    IEnumerable<long> selectedIds,
    IEnumerable<TEntity> records,
    Func<CancellationToken, Task>? flusher = null,
    PlatformDbContext? db = null)
    : base(selectedIds, records, flusher, db)
  {
  }

  public Dictionary<long, string?> Labels { get; } = [];
}
