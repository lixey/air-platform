namespace Platform.App.Tests.Internals;

internal sealed class MockBadFlowOrderFlowService(AppTestDbContext db) : StatusFlowService<TestInvoice>(db)
{
  public override Feature Feature => MockBadFlowModule.Orders;

  protected override void SetFlow(Flow flow)
  {
    flow.State(x => x.Status);
    flow.Status(1, "Draft", UiColor.Slate);
    flow.Initial(1);
  }

  public Transition BrokenJump => From(1).To(999);
}
