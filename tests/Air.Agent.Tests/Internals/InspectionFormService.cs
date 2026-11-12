namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestInspection>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetElements(Elements elements)
  {
    elements.UseStatusOf(new InspectionFlowService(db));
    elements.Add(i => i.Code).Required();
    elements.Add(i => i.Status);
    elements.Add(i => i.Notes);
    elements.AddChild<InspectionCheckpointGridService>().Key("checkpoints");
    elements.AddChild<InspectionMeasurementGridService>().Key("measurements");
    elements.AddChild<ReadOnlyChildNoteGridService>().Key("auditNotes");
    elements.AddChild<HiddenChildLogGridService>().Key("secretLogs");
  }
}
