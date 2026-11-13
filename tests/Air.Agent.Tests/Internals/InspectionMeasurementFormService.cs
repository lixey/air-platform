namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionMeasurementFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestMeasurement>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetParent() => Parent.Bind<InspectionFormService>(m => m.InspectionId);
  protected override void SetElements()
  {
    Elements.Add(m => m.Reading).Required();
    Elements.Add(m => m.Weight);
    Elements.Add(m => m.SortOrder);
  }
}
