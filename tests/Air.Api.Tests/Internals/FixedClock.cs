namespace Platform.Api.Tests.Internals;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
  public override DateTimeOffset GetUtcNow() => now;
}
