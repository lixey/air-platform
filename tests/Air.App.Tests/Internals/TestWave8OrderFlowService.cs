namespace Platform.App.Tests.Internals;

internal sealed class TestWave8OrderFlowService(AppTestDbContext db)
  : StatusFlowService<AppTestDbContext, TestInvoice>(db)
{
  public override Feature Feature => TestAppModule.Wave8Orders;

  protected override void SetFlow()
  {
    Flow.State(x => x.Status);
    Flow.Status(1, "Draft", UiColor.Slate);
    Flow.Status(2, "Approved", UiColor.Green);
    Flow.FinalStatus(3, "Rejected", UiColor.Red);
    Flow.Initial(1);
  }

  public Transition Approve => From(1)
    .To(2)
    .Allow(TestAppModule.ApproveInvoice)
    .When(x => x.PrintedCount >= 1, "Order must be printed before approval.")
    .Check((Move m, CancellationToken _) => Task.FromResult(m.Record.InternalMemo != "HOLD"), "Order is on credit hold.");

  public Transition Reject => FromOpen()
    .To(3)
    .Ask<TestAssignTagInput>(els =>
    {
      els.Add(x => x.TagId).AsReference<TestTagGridService>().Required();
      els.Add(x => x.Note).Required();
    })
    .Do((move, _) =>
    {
      move.Record.InternalMemo = move.InputOf<TestAssignTagInput>().Note;
      return Task.CompletedTask;
    });
}
