namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceLineGridService(AppTestDbContext db, PlatformAppService app) : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;
  protected override void SetParent(Parent parent) => parent.Bind<TestInvoiceFormService>(l => l.InvoiceId);
  protected override void SetNav(Nav nav)
  {
    nav.AddCreate<TestInvoiceLineFormService>();
    nav.AddPicker<TestTagGridService>(AttachTag);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<TestInvoiceLineFormService>();
    columns.Add(l => l.Title);
    columns.Add(l => l.Amount);
  }

  public Task<GridActionDto> AttachTag(GridPickRequest req, CancellationToken ct = default) =>
    AttachPickedAsync<TestTag>(req, (tag, line) =>
    {
      line.Title = tag.Name;
      line.Amount = 10m;
    }, ct);
}
