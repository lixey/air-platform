namespace Platform.App.Tests.Internals;

internal sealed class TestOrderDetailGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetParent(Parent parent) => parent.Bind<TestOrderMasterFormService>(l => l.InvoiceId);

  protected override void SetHooks(Hooks hooks) => hooks.AllowMultiDelete();

  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<TestOrderDetailFormService>();
    columns.UseStatusOf(new TestOrderLineFlowService(db));
    columns.Add(l => l.Title);
    columns.Add(l => l.Amount).Editable();
    columns.Add(l => l.TierCode);
  }
}
