namespace DemoApp.AgentProvider;

public sealed class AppAgentProviderFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAgentProvider>(db, app)
{
  public override Feature Feature => App.AgentProvider;

  protected override void SetElements() =>
    Elements.AddAgentProviderElements<AppAgentModelGridService>(IsNew, L);

  protected override void SetHooks() =>
    Hooks.AddAgentProviderHooks(db, L);
}
