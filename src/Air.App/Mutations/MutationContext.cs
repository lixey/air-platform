using Air.Data;

namespace Air.App.Mutations;

public class MutationContext(PlatformDbContext? db = null) : IMutationContext
{
  private readonly List<MutationError> _errors = [];

  public IReadOnlyList<MutationError> Errors => _errors;

  public void AddError(MutationError error)
  {
    _errors.Add(error);
  }

  public void AddError(string code, string message, long? recordId = null, string? path = null)
  {
    _errors.Add(new MutationError(code, message, path, recordId));
  }

  public virtual async Task FlushAsync(CancellationToken cancellationToken)
  {
    if (db != null)
    {
      await db.SaveChangesAsync(cancellationToken);
    }
  }

  public virtual Task LockAsync<TLocked>(CancellationToken cancellationToken, params long[] key) where TLocked : class
  {
    return Task.CompletedTask;
  }

  public virtual Task LockAsync<TLocked>(IEnumerable<long[]> keys, CancellationToken cancellationToken) where TLocked : class
  {
    return Task.CompletedTask;
  }
}
