namespace Platform.App.Tests.Internals;

internal sealed class TestOpenMetadataGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override async Task SetColumnsAsync(CancellationToken cancellationToken = default)
  {
    var count = await db.Invoices.CountAsync(cancellationToken);
    Columns.Add(r => r.Id).AsOpenForm<TestOpenMetadataFormService>();
    Columns.Add(r => r.Status);
    Columns.Add(r => r.PrintedCount).Label($"Printed ({count})");

    Columns.Add(r => r.Number).Label("Invoice #");
    var added = Columns.Items[^1];
    Columns.Items.RemoveAt(Columns.Items.Count - 1);
    Columns.Items.Insert(1, added);
    Columns.Remove("status");
  }

  protected override async Task SetNavAsync(CancellationToken cancellationToken = default)
  {
    var hasInvoices = await db.Invoices.AnyAsync(cancellationToken);
    Nav.AddCreate<TestOpenMetadataFormService>();
    Nav.WithoutExport();
    Nav.Customize(nav =>
    {
      if (hasInvoices)
      {
        nav.End ??= [];
        nav.End.Add(new NavLabelModel { Key = "summary", Label = "Ready" });
      }
    });
  }
}
