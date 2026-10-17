using Air.App.Modules;
using Air.Data.Entities;

namespace Air.App.Grid.Selection;

public sealed class GridBatchContext<TEntity> : GridSelectionContext<TEntity> where TEntity : class, IEntity
{
  public GridBatchContext(
    IEnumerable<long> selectedIds,
    IEnumerable<TEntity> records,
    Func<CancellationToken, Task>? flusher = null)
    : base(selectedIds, records, flusher)
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
