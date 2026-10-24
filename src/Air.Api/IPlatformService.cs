namespace Platform.Api;

/// <summary>
/// Universal marker and base contract for any platform-managed service (Grid, Form, Report, Screen, or custom domain/RPC service).
/// </summary>
public interface IPlatformService
{
  Feature Feature { get; }
  void AttachPlatformApp(PlatformAppService app) { }
}
