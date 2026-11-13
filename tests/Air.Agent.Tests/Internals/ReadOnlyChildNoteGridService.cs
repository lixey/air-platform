namespace Platform.Agent.Tests.Internals;

internal sealed class ReadOnlyChildNoteGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestAuditNote>(db, app)
{
  public override Feature Feature => AgentTestModule.ReadOnlyChildNotes;
  protected override void SetParent() => Parent.Bind<InspectionFormService>(n => n.InspectionId);
  protected override void SetNav() => Nav.AddCreate<ReadOnlyChildNoteFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<ReadOnlyChildNoteFormService>();
    Columns.Add(n => n.Summary);
  }
}
