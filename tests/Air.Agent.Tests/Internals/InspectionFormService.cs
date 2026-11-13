namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestInspection>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetQuery() => Query.Where(i => i.Code != "OTHER-DOCTYPE");
  protected override void SetElements()
  {
    Elements.UseStatusOf(new InspectionFlowService(db));
    Elements.Add(i => i.Code).Required();
    Elements.Add(i => i.Status);
    Elements.Add(i => i.Notes);
    Elements.AddChild<InspectionCheckpointGridService>().Key("checkpoints");
    Elements.AddChild<InspectionMeasurementGridService>().Key("measurements");
    Elements.AddChild<ReadOnlyChildNoteGridService>().Key("auditNotes");
    Elements.AddChild<HiddenChildLogGridService>().Key("secretLogs");
  }
}
