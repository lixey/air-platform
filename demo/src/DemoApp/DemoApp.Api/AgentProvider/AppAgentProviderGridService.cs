namespace DemoApp.AgentProvider;

public sealed class AppAgentProviderGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAgentProvider>(db, app)
{
  public override Feature Feature => App.AgentProvider;

  protected override void SetColumns() =>
    Columns.AddAgentProviderColumns<AppAgentProviderFormService>(L);

  protected override void SetQuery() =>
    Query.AddAgentProviderQuery(L);

  protected override void SetHooks() =>
    Hooks.AddAgentProviderHooks(db, L);

  protected override void SetActions() =>
    Actions.AddAgentProviderActions(db, L);
}
