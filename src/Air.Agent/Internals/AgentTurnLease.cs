namespace Platform.Agent.Internals;

internal sealed class AgentTurnLease(SemaphoreSlim semaphore) : IDisposable
{
  private int _disposed;

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 0)
    {
      semaphore.Release();
    }
  }
}
