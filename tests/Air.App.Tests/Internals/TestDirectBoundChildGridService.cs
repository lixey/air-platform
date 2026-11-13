namespace Platform.App.Tests.Internals;

internal sealed class TestDirectBoundChildGridService(AppTestDbContext db, PlatformAppService app)
  : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.DirectLines;
  protected override void SetParent() => Parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);
  protected override void SetColumns() => Columns.Add(l => l.Title);
}
