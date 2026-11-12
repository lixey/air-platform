namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionMeasurementGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestMeasurement>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetParent(Parent parent) => parent.Bind<InspectionFormService>(m => m.InspectionId);
  protected override void SetNav(Nav nav) => nav.AddCreate<InspectionMeasurementFormService>();
  protected override void SetHooks(Hooks hooks) => hooks.AllowMultiDelete();
  protected override void SetColumns(Columns columns)
  {
    columns.UseForm<InspectionMeasurementFormService>();
    columns.Add(m => m.Reading);
    columns.Add(m => m.Weight);
    columns.Add(m => m.SortOrder);
  }
}
