namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionCheckpointFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestCheckpoint>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetParent(Parent parent) => parent.Bind<InspectionFormService>(c => c.InspectionId);
  protected override void SetElements(Elements elements)
  {
    elements.Add(c => c.Code).Required();
    elements.Add(c => c.DueDate).Required();
  }
}
