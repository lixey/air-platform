namespace Platform.App.Tests.Internals;

internal sealed class MockBadReadOnlyEntityGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<AppLanguage>(db, app)
{
  public override Feature Feature => TestAppModule.Tags;

  protected override void SetColumns() => Columns.Add(x => x.Name);
}
