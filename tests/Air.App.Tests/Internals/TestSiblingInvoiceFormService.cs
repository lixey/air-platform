namespace Platform.App.Tests.Internals;

internal sealed class TestSiblingInvoiceFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.SiblingInvoices;

  protected override void SetElements() =>
    Elements.Add(i => i.Number).Required();
}
