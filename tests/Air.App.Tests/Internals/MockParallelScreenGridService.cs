namespace Platform.App.Tests.Internals;

internal sealed class MockParallelScreenGridService(MockParallelDbContext db, PlatformAppService? app = null)
  : GridService<TestTag>(db, app)
{
  public override Feature Feature => MockParallelScreenModule.Items;

  protected override void SetColumns() => Columns.Add(x => x.Id);
}
