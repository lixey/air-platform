namespace DemoApp.AgentModel;

public sealed class AppAgentModelFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentModel>(db, app)
{
  public override Feature Feature => App.AgentModel;

  protected override void SetElements() =>
    Elements.AddAgentModelElements(L);

  protected override void SetHooks() =>
    Hooks.AddAgentModelHooks(db, L);
}
