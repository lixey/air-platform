namespace Platform.Api.Tests.Internals;

internal sealed class InvalidPrimitiveEndpointProbe
{
  [Endpoint]
  public Task<string> InvalidPrimitiveParam(int count) => Task.FromResult(count.ToString());
}
