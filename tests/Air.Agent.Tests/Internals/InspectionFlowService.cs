namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionFlowService(AgentTestDbContext db)
  : StatusFlowService<AgentTestDbContext, AgentTestInspection>(db)
{
  public override Feature Feature => AgentTestModule.Inspections;

  protected override void SetFlow(Flow flow)
  {
    flow.State(x => x.Status);
    flow.Status(1, "Draft", UiColor.Slate);
    flow.Status(2, "Approved", UiColor.Green);
    flow.FinalStatus(3, "Rejected", UiColor.Red);
    flow.Initial(1);
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
