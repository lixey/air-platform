namespace Platform.Agent.Tests.Internals;

internal sealed class TestAgentMemoryFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AppAgentMemory>(db, app)
{
  public override Feature Feature => AgentTestModule.AgentMemories;

  protected override void SetElements() =>
    Elements.AddAgentMemoryElements(L);

  protected override void SetHooks() =>
    Hooks.AddAgentMemoryHooks(app.Time);
}
