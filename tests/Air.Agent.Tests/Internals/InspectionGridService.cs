namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestInspection>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetNav(Nav nav) => nav.AddCreate<InspectionFormService>();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<InspectionFormService>();
    columns.UseStatusOf(new InspectionFlowService(db));
    columns.Add(i => i.Code);
    columns.Add(i => i.Status);
    columns.Add(i => i.Notes);
  }
}
