namespace Platform.App.Tests.Internals;

internal sealed class MockBadSetActionGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestTag>(db, app)
{
  public override Feature Feature => MockBadSetActionModule.ReadOnlyItems;

  protected override void SetColumns(Columns columns) => columns.Add(x => x.Name);

  protected override void SetActions(Actions actions)
  {
    actions.Add("MutateOnReadOnly").AllowEdit();
  }
}
