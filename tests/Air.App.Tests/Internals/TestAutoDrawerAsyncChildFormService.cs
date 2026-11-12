namespace Platform.App.Tests.Internals;

internal sealed class TestAutoDrawerAsyncChildFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override Task SetElementsAsync(Elements elements, CancellationToken ct)
  {
    elements.Add(i => i.Number).Required();
    elements.AddChild<TestInvoiceLineGridService>();
    return Task.CompletedTask;
  }
}
