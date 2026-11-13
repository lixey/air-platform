namespace Platform.App.Tests.Internals;

internal sealed class TestOrderDetailFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetParent() => Parent.Bind<TestOrderMasterFormService>(l => l.InvoiceId);

  protected override void SetElements()
  {
    Elements.UseStatusOf(new TestOrderLineFlowService(db));
    Elements.Add(l => l.Title).Required();
    Elements.Add(l => l.Amount);
    Elements.Add(l => l.TierCode);
  }
}
