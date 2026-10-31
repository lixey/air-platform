namespace Platform.App.Tests.Internals;

internal sealed class EmptyServiceProvider : IServiceProvider
{
  public object? GetService(Type serviceType) => null;
}
