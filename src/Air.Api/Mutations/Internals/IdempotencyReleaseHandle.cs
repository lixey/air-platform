namespace Platform.Api.Mutations.Internals;

internal sealed class IdempotencyReleaseHandle(
  string lockId,
  IdempotencyKeyLock entry,
  Action<string, IdempotencyKeyLock> onRelease) : IDisposable
{
  private int _disposed;

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 0)
    {
      try
      {
        entry.Semaphore.Release();
      }
      finally
      {
        onRelease(lockId, entry);
      }
    }
  }
}
