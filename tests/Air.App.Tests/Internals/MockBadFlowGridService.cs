namespace Platform.App.Tests.Internals;

internal sealed class MockBadFlowGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => MockBadFlowModule.Orders;

  protected override void SetColumns() => Columns.Add(x => x.Number);
}
