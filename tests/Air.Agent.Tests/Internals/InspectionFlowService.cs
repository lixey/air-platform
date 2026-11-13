namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionFlowService(AgentTestDbContext db)
  : StatusFlowService<AgentTestDbContext, AgentTestInspection>(db)
{
  public override Feature Feature => AgentTestModule.Inspections;

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
    .When(x => x.Code != "BLOCKED", "Blocked inspection cannot be approved.");

  public Transition Reject => From(1, 2)
    .To(3)
    .Ask<InspectionRejectAskInput>(els => els.Add(x => x.Reason).Required())
    .Do((move, _) =>
    {
      move.Record.Notes = move.InputOf<InspectionRejectAskInput>().Reason;
      return Task.CompletedTask;
    });
}
