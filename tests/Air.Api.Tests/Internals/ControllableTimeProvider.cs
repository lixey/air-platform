namespace Platform.Api.Tests.Internals;

internal sealed class ControllableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
{
  private DateTimeOffset _now = initialUtc;

  public override DateTimeOffset GetUtcNow() => _now;

  public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
