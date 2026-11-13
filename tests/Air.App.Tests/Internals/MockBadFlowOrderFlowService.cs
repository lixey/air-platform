namespace Platform.App.Tests.Internals;

internal sealed class MockBadFlowOrderFlowService(AppTestDbContext db) : StatusFlowService<TestInvoice>(db)
{
  public override Feature Feature => MockBadFlowModule.Orders;

  protected override void SetFlow()
  {
    Flow.State(x => x.Status);
    Flow.Status(1, "Draft", UiColor.Slate);
    Flow.Initial(1);
  }

  public Transition BrokenJump => From(1).To(999);
}
