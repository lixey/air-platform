namespace Platform.App.Tests.Internals;

internal sealed class TestConfirmAsyncGridWithFallbackService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice, TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetColumns() =>
    Columns.Add(r => r.Number);

  protected override void SetActions() =>
    Actions.UseActionsOf<TestConfirmAsyncWithFallbackFormService>();
}
