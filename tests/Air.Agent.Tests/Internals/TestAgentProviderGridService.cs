namespace Platform.Agent.Tests.Internals;

internal sealed class TestAgentProviderGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AppAgentProvider>(db, app)
{
  public override Feature Feature => AgentTestModule.AgentProviders;

  protected override void SetNav() => Nav.AddCreate<TestAgentProviderFormService>();

  protected override void SetColumns() =>
    Columns.AddAgentProviderColumns<TestAgentProviderFormService>(L);

  protected override void SetQuery() =>
    Query.AddAgentProviderQuery(L);

  protected override void SetHooks() =>
    Hooks.AddAgentProviderHooks(db, L);

  protected override void SetActions() =>
    Actions.AddAgentProviderActions(db, L);
}
