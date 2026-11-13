namespace Platform.App.Tests.Internals;

internal sealed class TestRequiredParentLineFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetParent(Parent parent) =>
    parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);

  protected override void SetElements(Elements elements)
  {
    var parent = RequiredParent<TestInvoice>();
    elements.Add(l => l.Amount).AsMoney().Disabled(parent.Status == 2);
  }

  public TestInvoice ReadRequiredParent() => RequiredParent<TestInvoice>();
}
