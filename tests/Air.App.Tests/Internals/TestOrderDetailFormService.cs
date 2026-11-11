namespace Platform.App.Tests.Internals;

internal sealed class TestOrderDetailFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestInvoiceLine>(db, app)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetParent(Parent parent) => parent.Bind<TestOrderMasterFormService>(l => l.InvoiceId);

  protected override void SetElements(Elements elements)
  {
    elements.UseStatusOf(new TestOrderLineFlowService(db));
    elements.Add(l => l.Title).Required();
    elements.Add(l => l.Amount);
    elements.Add(l => l.TierCode);
  }
}
