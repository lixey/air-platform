namespace Platform.App.Tests.Internals;

internal abstract class TestDualColumnsAbstractGrid(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  protected override void SetColumns() =>
    Columns.Add(r => r.Number);

  protected override Task SetColumnsAsync(CancellationToken cancellationToken = default)
  {
    Columns.Add(r => r.PrintedCount);
    return Task.CompletedTask;
  }
}
