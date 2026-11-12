namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionCheckpointGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestCheckpoint>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetParent(Parent parent) => parent.Bind<InspectionFormService>(c => c.InspectionId);
  protected override void SetNav(Nav nav) => nav.AddCreate<InspectionCheckpointFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<InspectionCheckpointFormService>();
    columns.Add(c => c.Code);
    columns.Add(c => c.DueDate);
  }
}
