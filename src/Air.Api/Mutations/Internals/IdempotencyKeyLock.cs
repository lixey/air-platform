namespace Platform.Api.Mutations.Internals;

internal sealed class IdempotencyKeyLock
{
  private int _refCount;
  public SemaphoreSlim Semaphore { get; } = new(1, 1);

  public bool TryAddRef()
  {
    while (true)
    {
      var current = Volatile.Read(ref _refCount);
      if (current < 0)
      {
        return false;
      }

      if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
      {
        return true;
      }
    }
  }

  public bool ReleaseRef()
  {
    while (true)
    {
      var current = Volatile.Read(ref _refCount);
      if (current <= 1)
      {
        if (Interlocked.CompareExchange(ref _refCount, -1, current) == current)
        {
          return true;
        }
      }
      else if (Interlocked.CompareExchange(ref _refCount, current - 1, current) == current)
      {
        return false;
      }
    }
  }
}
