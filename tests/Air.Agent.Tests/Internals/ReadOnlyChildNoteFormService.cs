namespace Platform.Agent.Tests.Internals;

internal sealed class ReadOnlyChildNoteFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestAuditNote>(db, app)
{
  public override Feature Feature => AgentTestModule.ReadOnlyChildNotes;
  protected override void SetParent() => Parent.Bind<InspectionFormService>(n => n.InspectionId);
  protected override void SetElements() => Elements.Add(n => n.Summary).Required();
}
