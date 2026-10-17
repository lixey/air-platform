namespace Air.App.Mutations;

public interface IMutationContext
{
  IReadOnlyList<MutationError> Errors { get; }
  void AddError(MutationError error);
  void AddError(string code, string message, long? recordId = null, string? path = null);
  Task FlushAsync(CancellationToken cancellationToken);
  Task LockAsync<TLocked>(CancellationToken cancellationToken, params long[] key) where TLocked : class;
  Task LockAsync<TLocked>(IEnumerable<long[]> keys, CancellationToken cancellationToken) where TLocked : class;
}
