namespace Platform.App.Tests.Internals;

internal sealed class TestAutoDrawerAsyncChildFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override async Task SetElementsAsync(CancellationToken ct)
  {
    await Task.Yield();
    Elements.Add(i => i.Number).Required();
    Elements.AddChild<TestInvoiceLineGridService>();
  }
}
