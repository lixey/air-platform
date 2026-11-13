namespace Platform.App.Tests.Internals;

internal sealed class TestAutoDrawerAsyncChildFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override async Task SetElementsAsync(Elements elements, CancellationToken ct)
  {
    await Task.Yield();
    elements.Add(i => i.Number).Required();
    elements.AddChild<TestInvoiceLineGridService>();
  }
}
