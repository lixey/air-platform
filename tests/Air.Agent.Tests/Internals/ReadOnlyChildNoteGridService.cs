namespace Platform.Agent.Tests.Internals;

internal sealed class ReadOnlyChildNoteGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestAuditNote>(db, app)
{
  public override Feature Feature => AgentTestModule.ReadOnlyChildNotes;
  protected override void SetParent(Parent parent) => parent.Bind<InspectionFormService>(n => n.InspectionId);
  protected override void SetNav(Nav nav) => nav.AddCreate<ReadOnlyChildNoteFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<ReadOnlyChildNoteFormService>();
    columns.Add(n => n.Summary);
  }
}
