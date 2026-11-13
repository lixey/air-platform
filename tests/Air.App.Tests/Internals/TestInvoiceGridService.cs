namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceGridService(AppTestDbContext db, PlatformAppService? app = null) : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;
  protected override void SetQuery() => Query.Where(i => !i.IsHidden);
  protected override void SetNav() => Nav.AddCreate<TestInvoiceFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<TestInvoiceFormService>();
    Columns.UseStatusOf(new TestInvoiceStatusFlow(db));
    Columns.Add(i => i.Number).Editable();
    Columns.Add(i => i.Status);
    Columns.Add(i => i.PrintedCount).Sortable(false).Filterable(false);
  }
}
