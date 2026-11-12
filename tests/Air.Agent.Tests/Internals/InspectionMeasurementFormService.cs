namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionMeasurementFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestMeasurement>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetParent(Parent parent) => parent.Bind<InspectionFormService>(m => m.InspectionId);
  protected override void SetElements(Elements elements)
  {
    elements.Add(m => m.Reading).Required();
    elements.Add(m => m.Weight);
    elements.Add(m => m.SortOrder);
  }
}
