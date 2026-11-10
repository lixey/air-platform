namespace Platform.App.Screens;

public interface IScreenService : IPlatformService
{
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);
}
