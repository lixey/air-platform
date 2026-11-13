namespace Platform.Agent.Tests.Internals;

internal sealed class CategoryFormService(AgentTestDbContext db, PlatformAppService app) : FormService<AgentTestCategory>(db, app)
{
  public override Feature Feature => AgentTestModule.Categories;
  protected override void SetElements() => Elements.Add(c => c.Name).Required();
}
