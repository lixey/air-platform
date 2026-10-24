using System.Collections.Immutable;

namespace Platform.App.Grid.Selection;

public abstract class GridSelectionContext<TEntity> : IMutationContext where TEntity : class
{
  private readonly List<MutationError> _errors = [];
  private readonly List<MutationError> _warnings = [];
  private readonly Func<CancellationToken, Task>? _flusher;
  private readonly PlatformDbContext? _db;

  protected GridSelectionContext(
    IEnumerable<long> selectedIds,
    IEnumerable<TEntity> records,
    Func<CancellationToken, Task>? flusher = null,
    PlatformDbContext? db = null)
  {
    SelectedIds = selectedIds.Distinct().ToImmutableArray();
    Records = records.ToImmutableArray();
    _flusher = flusher;
    _db = db;
  }

  public ImmutableArray<long> SelectedIds { get; }

  public ImmutableArray<TEntity> Records { get; }

  public IReadOnlyList<MutationError> Errors => _errors;

  public IReadOnlyList<MutationError> Warnings => _warnings;

  public void AddError(string code, string message, long? recordId = null, string? path = null) =>
    _errors.Add(new MutationError(code, message, RecordId: recordId, Path: path));

  public void AddError(MutationError error) => _errors.Add(error);

  public void Warn(MutationError warning) => _warnings.Add(warning);

  public void Warn(string message, string? code = null, long? recordId = null, string? path = null) =>
    _warnings.Add(new MutationError(code ?? "grid.warning", message, Path: path, RecordId: recordId));

  public void Refuse(string message, long? recordId = null, string? path = null) =>
    AddError("refused", message, recordId, path);

  public Task FlushAsync(CancellationToken cancellationToken) =>
    _flusher != null ? _flusher(cancellationToken) : Task.CompletedTask;

  public Task LockAsync<TLocked>(CancellationToken cancellationToken, params long[] key) where TLocked : class =>
    MutationContext.AcquireLocksAsync(_db, typeof(TLocked), [key], cancellationToken);

  public Task LockAsync<TLocked>(IEnumerable<long[]> keys, CancellationToken cancellationToken) where TLocked : class =>
    MutationContext.AcquireLocksAsync(_db, typeof(TLocked), keys, cancellationToken);
}
