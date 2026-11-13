namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceFormService(AppTestDbContext db, PlatformAppService? app = null) : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;
  protected override void SetQuery() => Query.From(db.Invoices).Where(i => !i.IsHidden);
  protected override void SetElements()
  {
    Elements.UseStatusOf(new TestInvoiceStatusFlow(db));
    Elements.Add(i => i.Number).Required().DisabledWhen(i => i.Status == 2);
    Elements.Add(i => i.Status);
    Elements.Add(i => i.ApiSecret).InputOnly().AsPassword();
    Elements.Add(i => i.TagIds).AsReferences<TestTagGridService, TestInvoiceTag>(j => j.InvoiceId, j => j.TagId);
    Elements.AddChild<TestInvoiceLineGridService>();
  }
}
