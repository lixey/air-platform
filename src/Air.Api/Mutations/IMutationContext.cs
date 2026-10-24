namespace Platform.Api.Mutations;

public interface IMutationContext
{
  IReadOnlyList<MutationError> Errors { get; }
  IReadOnlyList<MutationError> Warnings { get; }
  void AddError(MutationError error);
  void AddError(string code, string message, long? recordId = null, string? path = null);
  void Warn(MutationError warning);
  void Warn(string message, string? code = null, long? recordId = null, string? path = null);
  Task FlushAsync(CancellationToken cancellationToken);
  Task LockAsync<TLocked>(CancellationToken cancellationToken, params long[] key) where TLocked : class;
  Task LockAsync<TLocked>(IEnumerable<long[]> keys, CancellationToken cancellationToken) where TLocked : class;
}
