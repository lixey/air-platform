namespace Platform.App.Tests.Internals;

internal abstract class TestDualColumnsAbstractGrid(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  protected override void SetColumns(Columns columns) =>
    columns.Add(r => r.Number);

  protected override void SetColumns(Columns<TestInvoice> columns) =>
    columns.Add(r => r.PrintedCount);
}
