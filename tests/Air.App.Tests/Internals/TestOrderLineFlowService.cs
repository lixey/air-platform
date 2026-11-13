namespace Platform.App.Tests.Internals;

internal sealed class TestOrderLineFlowService(AppTestDbContext db)
  : StatusFlowService<AppTestDbContext, TestInvoiceLine>(db)
{
  public override Feature Feature => TestAppModule.OrderDocs;

  protected override void SetFlow()
  {
    Flow.State(x => x.TierCode);
    Flow.Status(1, "Pending", UiColor.Blue);
    Flow.FinalStatus(2, "Fulfilled", UiColor.Green);
    Flow.Initial(1);
  }

  public Transition Fulfill => FromOpen().To(2);
}
