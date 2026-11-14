namespace Platform.Data.Tests.Internals;

internal sealed class FixedDataTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
  public override DateTimeOffset GetUtcNow() => utcNow;
}
