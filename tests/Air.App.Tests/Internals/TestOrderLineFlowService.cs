namespace Platform.App.Tests.Internals;

internal sealed class TestOrderLineFlowService(AppTestDbContext db)
  : StatusFlowService<AppTestDbContext, TestInvoiceLine>(db)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetFlow(Flow flow)
  {
    flow.State(x => x.TierCode);
    flow.Status(1, "Pending", UiColor.Blue);
    flow.FinalStatus(2, "Fulfilled", UiColor.Green);
    flow.Initial(1);
  }

  public Transition Fulfill => FromOpen().To(2);
}
