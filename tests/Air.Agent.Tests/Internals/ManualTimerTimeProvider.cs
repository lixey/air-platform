namespace Platform.Agent.Tests.Internals;

internal sealed class ManualTimerTimeProvider : TimeProvider, ITimer
{
  private TimerCallback? _callback;
  private object? _state;

  public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
  {
    _callback = callback;
    _state = state;
    return this;
  }

  public void FireTick() => _callback?.Invoke(_state);

  public bool Change(TimeSpan dueTime, TimeSpan period) => true;

  public void Dispose() => _callback = null;

  public ValueTask DisposeAsync()
  {
    _callback = null;
    return ValueTask.CompletedTask;
  }
}
