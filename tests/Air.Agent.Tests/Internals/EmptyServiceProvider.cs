namespace Platform.Agent.Tests.Internals;

internal sealed class EmptyServiceProvider : IServiceProvider
{
  public object? GetService(Type serviceType) => null;
}
