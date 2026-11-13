namespace Platform.Api;

/// <summary>
/// Universal contract for any platform-managed service (Grid, Form, Report, Screen, Metadata, or custom domain/RPC service).
/// </summary>
public interface IPlatformService
{
  Feature Feature { get; }
  IEnumerable<string> GetDeclaredActions() => [];
  IEnumerable<string> GetDynamicEndpointNames(ModuleCatalog catalog) => [];

  Task<IResult?> ExecuteDynamicEndpointAsync(
    string method,
    HttpRequest httpRequest,
    JsonElement? body,
    IServiceProvider services,
    CancellationToken ct) =>
    Task.FromResult<IResult?>(null);

  void BindContext(PlatformAppService? app) { }
  void ValidateConfiguration(ModuleCatalog catalog) { }
}
