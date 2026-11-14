namespace Platform.App.Tests.Internals;

internal sealed class TestOverviewScreenService(PlatformAppService? app = null) : ScreenService(app)
{
  public override Feature Feature => TestAppModule.OverviewScreen;

  public PlatformAppService? CurrentAppService => PlatformApp;
  public long? LastSeenUserId { get; private set; }

  protected override void SetPanels()
  {
    LastSeenUserId = User?.UserId;
    Panels.Column();
  }
}
