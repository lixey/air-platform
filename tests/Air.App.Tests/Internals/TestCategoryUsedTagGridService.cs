namespace Platform.App.Tests.Internals;

internal sealed class TestCategoryUsedTagGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestTag>(db, app)
{
  public override Feature Feature => TestAppModule.CategoryTags;

  protected override void SetColumns(Columns columns)
  {
    columns.Add(x => x.Id);
    columns.Add(x => x.Name);
  }

  protected override void SetHooks(Hooks hooks) =>
    hooks.PreventDeleteWhenUsedBy<TestTag>(t => t.CategoryId);
}
