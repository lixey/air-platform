namespace Platform.App.Tests.Internals;

internal sealed class TestOrderDetailGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetParent() => Parent.Bind<TestOrderMasterFormService>(l => l.InvoiceId);

  protected override void SetHooks() => Hooks.AllowMultiDelete();

  protected override void SetColumns()
  {
    Columns.UseForm<TestOrderDetailFormService>();
    Columns.UseStatusOf(new TestOrderLineFlowService(db));
    Columns.Add(l => l.Title);
    Columns.Add(l => l.Amount).Editable();
    Columns.Add(l => l.TierCode);
  }
}
