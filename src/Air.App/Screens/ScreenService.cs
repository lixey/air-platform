namespace Platform.App.Screens;

public abstract class ScreenService(PlatformAppService? app = null) : PlatformService(app), IScreenService
{
  public abstract Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);
}
