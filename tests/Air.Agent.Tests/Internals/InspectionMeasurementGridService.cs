namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionMeasurementGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AgentTestMeasurement>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetParent() => Parent.Bind<InspectionFormService>(m => m.InspectionId);
  protected override void SetNav() => Nav.AddCreate<InspectionMeasurementFormService>();
  protected override void SetHooks() => Hooks.AllowMultiDelete();
  protected override void SetColumns()
  {
    Columns.UseForm<InspectionMeasurementFormService>();
    Columns.Add(m => m.Reading);
    Columns.Add(m => m.Weight);
    Columns.Add(m => m.SortOrder);
  }
}
