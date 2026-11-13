namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceStatusFlow(AppTestDbContext db) : StatusFlowService<TestInvoice>(db)
{
  public override Feature Feature => TestAppModule.Invoices;

  protected override void SetFlow()
  {
    Flow.State(x => x.Status);
    Flow.Status(1, "New", UiColor.Blue);
    Flow.Status(2, "Approved", UiColor.Green);
    Flow.Initial(1);
  }

  public Transition Approve => From(1).To(2).Allow(TestAppModule.ApproveInvoice);
}
