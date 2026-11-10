namespace Platform.Data.Internals;

internal sealed class DisposableScope(Action onDispose) : IDisposable
{
  public void Dispose() => onDispose();
}
