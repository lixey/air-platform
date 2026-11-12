namespace Platform.Agent.Tests.Internals;

internal sealed class HiddenChildLogGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestAuditNote>(db, app)
{
  public override Feature Feature => AgentTestModule.Secrets;
  protected override void SetParent(Parent parent) => parent.Bind<InspectionFormService>(n => n.InspectionId);
  protected override void SetColumns(Columns columns) => columns.Add(n => n.Summary);
}
