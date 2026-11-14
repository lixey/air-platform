namespace DemoApp.AgentMemory;

public sealed class AppAgentMemoryFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentMemory>(db, app)
{
  public override Feature Feature => App.AgentMemory;

  protected override void SetElements() =>
    Elements.AddAgentMemoryElements(L);

  protected override void SetHooks() =>
    Hooks.AddAgentMemoryHooks(Time);
}
