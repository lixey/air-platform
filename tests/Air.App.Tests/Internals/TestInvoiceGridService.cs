namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceGridService(AppTestDbContext db, PlatformAppService app) : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;
  protected override void SetQuery(Query query) => query.Where(i => !i.IsHidden);
  protected override void SetNav(Nav nav) => nav.AddCreate<TestInvoiceFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<TestInvoiceFormService>();
    columns.UseStatusOf(new TestInvoiceStatusFlow(db));
    columns.Add(i => i.Number).Editable();
    columns.Add(i => i.Status);
  }
}
