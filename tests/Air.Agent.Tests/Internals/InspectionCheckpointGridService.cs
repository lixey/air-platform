namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionCheckpointGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestCheckpoint>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetParent() => Parent.Bind<InspectionFormService>(c => c.InspectionId);
  protected override void SetNav() => Nav.AddCreate<InspectionCheckpointFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<InspectionCheckpointFormService>();
    Columns.Add(c => c.Code);
    Columns.Add(c => c.DueDate);
  }
}
