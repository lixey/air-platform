namespace Platform.Agent.Tests.Internals;

internal sealed class ReadOnlyAgentCategoryFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestCategory>(db, app)
{
  public override Feature Feature => AgentTestModule.ReadOnlyCategories;
  protected override void SetElements(Elements elements) => elements.Add(c => c.Name).Required();
}
