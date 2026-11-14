namespace Platform.Agent.Tests.Internals;

internal sealed class TestAgentProviderFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AppAgentProvider>(db, app)
{
  public override Feature Feature => AgentTestModule.AgentProviders;

  protected override void SetElements() =>
    Elements.AddAgentProviderElements<TestAgentModelGridService>(IsNew, L);

  protected override void SetHooks() =>
    Hooks.AddAgentProviderHooks(db, L);
}
