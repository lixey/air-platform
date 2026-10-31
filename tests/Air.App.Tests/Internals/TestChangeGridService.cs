namespace Platform.App.Tests.Internals;

internal sealed class TestChangeGridService(AppTestDbContext db, PlatformAppService app) : ChangeGridService(db, null, app)
{
  public override Feature Feature => TestAppModule.Changes;
}
