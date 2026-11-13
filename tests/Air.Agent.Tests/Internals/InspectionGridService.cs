namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestInspection>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetQuery() => Query.Where(i => i.Code != "OTHER-DOCTYPE");
  protected override void SetNav() => Nav.AddCreate<InspectionFormService>();
  protected override void SetColumns()
  {
    Columns.UseForm<InspectionFormService>();
    Columns.UseStatusOf(new InspectionFlowService(db));
    Columns.Add(i => i.Code);
    Columns.Add(i => i.Status);
    Columns.Add(i => i.Notes);
  }
}
