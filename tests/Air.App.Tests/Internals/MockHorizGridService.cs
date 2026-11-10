namespace Platform.App.Tests.Internals;

internal sealed class MockHorizGridService(MockParallelDbContext db, PlatformAppService? app = null)
  : GridService<TestTag>(db, app)
{
  public override Feature Feature => MockHorizModule.Items;

  protected override void SetColumns(Columns columns) =>
    columns.Add(x => x.Id).AsOpenForm<TestInvoiceFormService>();
}
