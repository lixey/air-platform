namespace Platform.App.Tests.Internals;

internal abstract class TestDualElementsAbstractForm(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  protected override void SetElements() =>
    Elements.Add(e => e.Number);

  protected override async Task SetElementsAsync(CancellationToken ct)
  {
    await Task.Yield();
    Elements.Add(e => e.Number);
  }
}
