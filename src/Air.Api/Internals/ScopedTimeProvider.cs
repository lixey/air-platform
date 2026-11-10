namespace Platform.Api.Internals;

internal sealed class ScopedTimeProvider(TimeProvider inner, TimeZoneInfo localTimeZone) : TimeProvider
{
  public override TimeZoneInfo LocalTimeZone => localTimeZone;

  public override long TimestampFrequency => inner.TimestampFrequency;

  public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

  public override long GetTimestamp() => inner.GetTimestamp();

  public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
    inner.CreateTimer(callback, state, dueTime, period);
}
