namespace Platform.App.Tests.Internals;

internal sealed class MockBadConfirmAsyncGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice, TestInvoice>(db, app)
{
  public override Feature Feature => MockBadConfirmAsyncModule.Items;

  protected override void SetColumns() =>
    Columns.Add(r => r.Number);

  protected override void SetActions() =>
    Actions.UseActionsOf<TestConfirmAsyncFormService>();
}
