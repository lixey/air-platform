namespace Platform.App.Tests.Internals;

internal sealed class MockLockedInvoiceFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override bool IsReadOnly => base.IsReadOnly || CurrentEntity is { PrintedCount: >= 99 };

  protected override void SetQuery() => Query.From(db.Invoices).Where(i => !i.IsHidden);

  protected override void SetElements()
  {
    Elements.UseStatusOf(new TestInvoiceStatusFlow(db));
    Elements.Add(i => i.Number).Required();
    Elements.Add(i => i.Status);
  }
}
