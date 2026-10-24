namespace Platform.Api.Host;

/// <summary>
/// Pluggable endpoint dispatcher allowing higher-level layers (such as <c>Air.App</c> for Grids, Forms, Reports, and Screens)
/// to handle specialized service types before falling back to universal <see cref="IPlatformService"/> RPC method dispatch.
/// </summary>
public interface IServiceEndpointDispatcher
{
  bool CanDispatch(Type serviceType);

  Task<IResult> DispatchAsync(
    Type serviceType,
    string serviceName,
    string method,
    string? subMethod,
    Feature? feature,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct);
}
