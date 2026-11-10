namespace Platform.App.Tests.Internals;

internal sealed class TestOverviewScreenService(PlatformAppService? app = null) : ScreenService(app)
{
  public override Feature Feature => TestAppModule.OverviewScreen;

  public PlatformAppService? CurrentAppService => PlatformApp;
  public long? LastSeenUserId { get; private set; }

  public override Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default)
  {
    LastSeenUserId = User?.UserId;
    return Task.FromResult(new ScreenDto
    {
      Layout = PanelLayout.Column(servicePath).Build()
    });
  }
}
