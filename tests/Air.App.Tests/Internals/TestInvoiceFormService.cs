namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceFormService(AppTestDbContext db, PlatformAppService app) : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;
  protected override void SetQuery(Query query) => query.From(db.Invoices).Where(i => !i.IsHidden);
  protected override void SetElements(Elements elements)
  {
    elements.UseStatusOf(new TestInvoiceStatusFlow(db));
    elements.Add(i => i.Number).Required();
    elements.Add(i => i.Status);
    elements.Add(i => i.TagIds).AsReferences<TestTagGridService, TestInvoiceTag>(j => j.InvoiceId, j => j.TagId);
    elements.AddChild<TestInvoiceLineGridService>();
  }
}
