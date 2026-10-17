using System.Collections.Immutable;
using Air.App.Mutations;

namespace Air.App.Grid.Selection;

public abstract class GridSelectionContext<TEntity> : IMutationContext where TEntity : class
{
  private readonly List<MutationError> _errors = [];
  private readonly Func<CancellationToken, Task>? _flusher;

  protected GridSelectionContext(
    IEnumerable<long> selectedIds,
    IEnumerable<TEntity> records,
    Func<CancellationToken, Task>? flusher = null)
  {
    SelectedIds = selectedIds.Distinct().ToImmutableArray();
    Records = records.ToImmutableArray();
    _flusher = flusher;
  }

  public ImmutableArray<long> SelectedIds { get; }

  public ImmutableArray<TEntity> Records { get; }

  public IReadOnlyList<MutationError> Errors => _errors;

  public void AddError(string code, string message, long? recordId = null, string? path = null) =>
    _errors.Add(new MutationError(code, message, RecordId: recordId, Path: path));

  public void AddError(MutationError error) => _errors.Add(error);

  public Task FlushAsync(CancellationToken cancellationToken) =>
    _flusher != null ? _flusher(cancellationToken) : Task.CompletedTask;

  public Task LockAsync<TLocked>(CancellationToken cancellationToken, params long[] key) where TLocked : class =>
    Task.CompletedTask;

  public Task LockAsync<TLocked>(IEnumerable<long[]> keys, CancellationToken cancellationToken) where TLocked : class =>
    Task.CompletedTask;
}
