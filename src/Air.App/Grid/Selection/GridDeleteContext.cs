namespace Air.App.Grid.Selection;

public sealed class GridDeleteContext<TEntity> : GridSelectionContext<TEntity> where TEntity : class
{
  public GridDeleteContext(
    IEnumerable<long> selectedIds,
    IEnumerable<TEntity> records,
    Func<CancellationToken, Task>? flusher = null)
    : base(selectedIds, records, flusher)
  {
  }

  public Dictionary<long, string?> Labels { get; } = [];
}
