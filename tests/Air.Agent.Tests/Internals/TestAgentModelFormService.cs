namespace Platform.Agent.Tests.Internals;

internal sealed class TestAgentModelFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AppAgentModel>(db, app)
{
  public override Feature Feature => AgentTestModule.AgentModels;

  protected override void SetElements() =>
    Elements.AddAgentModelElements(L);

  protected override void SetHooks() =>
    Hooks.AddAgentModelHooks(db, L);
}
