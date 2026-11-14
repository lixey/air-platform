namespace Platform.App.Tests.Internals;

internal sealed class TestChangeGridService(AppTestDbContext db, PlatformAppService? app = null) : ChangeGridService(db, app)
{
  public override Feature Feature => TestAppModule.Changes;
}
