namespace Platform.App.Tests.Internals;

internal sealed class TestOpenMetadataFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoice>(db, app)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetElements()
  {
    Elements.Add(e => e.Status);
    Elements.Add(e => e.PrintedCount);
    Elements.Add(e => e.Number).Label("Number First");
    var numberEl = Elements.Items[^1];
    Elements.Items.RemoveAt(Elements.Items.Count - 1);
    Elements.Items.Insert(0, numberEl);
    Elements.Remove("status");
  }

  protected override async Task SetNavAsync(CancellationToken cancellationToken = default)
  {
    var total = await db.Invoices.CountAsync(cancellationToken);
    Nav.WithoutSaveAndClose();
    Nav.Customize(nav =>
    {
      nav.End ??= [];
      nav.End.Add(new NavLabelModel { Key = "count", Label = $"Total: {total}" });
    });
  }
}
