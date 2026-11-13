namespace Platform.App.Tests.Internals;

internal sealed class TestTagGridService(AppTestDbContext db, PlatformAppService app) : GridService<TestTag>(db, app)
{
  public override Feature Feature => TestAppModule.Tags;
  protected override void SetQuery() => Query.Where(t => t.IsActive);

  protected override void SetColumns()
  {
    Columns.Add(t => t.Name);
    Columns.Add(t => t.DefaultPrice);
  }
}
