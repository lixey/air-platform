namespace Platform.Mcp.Internals;

internal sealed class McpTurnScope(Action onDispose) : IDisposable
{
  private int _disposed;

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 0)
    {
      onDispose();
    }
  }
}
