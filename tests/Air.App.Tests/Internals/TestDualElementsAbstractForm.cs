namespace Platform.App.Tests.Internals;

internal abstract class TestDualElementsAbstractForm(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  protected override void SetElements(Elements elements) =>
    elements.Add(e => e.Number);

  protected override async Task SetElementsAsync(Elements elements, CancellationToken ct)
  {
    await Task.Yield();
    elements.Add(e => e.Number);
  }
}
