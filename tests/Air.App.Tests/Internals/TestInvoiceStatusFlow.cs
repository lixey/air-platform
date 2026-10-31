namespace Platform.App.Tests.Internals;

internal sealed class TestInvoiceStatusFlow(AppTestDbContext db) : StatusFlowService<TestInvoice>(db)
{
  protected override void SetFlow(Flow flow)
  {
    flow.State(x => x.Status);
    flow.Status(1, "New", UiColor.Blue);
    flow.Status(2, "Approved", UiColor.Green);
    flow.Initial(1);
  }

  public Transition Approve => From(1).To(2).RequireRight(TestAppModule.ApproveInvoice);
}
