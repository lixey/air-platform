namespace Platform.App.Tests.Internals;

internal sealed class TestWave8OrderLineGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.Wave8Orders;

  protected override void SetParent() => Parent.Bind<TestWave8OrderFormService>(l => l.InvoiceId);

  protected override void SetNav() => Nav.AddCreate<TestWave8OrderLineFormService>();

  protected override void SetHooks() => Hooks.AllowMultiDelete();

  protected override void SetColumns()
  {
    Columns.UseForm<TestWave8OrderLineFormService>();
    Columns.Add(l => l.Title);
    Columns.Add(l => l.Amount);
    Columns.Add(l => l.TagId);
  }
}
