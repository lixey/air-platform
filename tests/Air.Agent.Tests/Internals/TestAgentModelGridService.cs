namespace Platform.Agent.Tests.Internals;

internal sealed class TestAgentModelGridService(
  AgentTestDbContext db,
  PlatformAppService app,
  IAgentModelSynchronizer? synchronizer = null) : GridService<AppAgentModel>(db, app)
{
  public override Feature Feature => AgentTestModule.AgentModels;

  protected override void SetNav() => Nav.AddCreate<TestAgentModelFormService>();

  protected override void SetColumns() =>
    Columns.AddAgentModelColumns<TestAgentModelFormService>(L);

  protected override void SetQuery() =>
    Query.AddAgentModelQuery(db, ParentId, L);

  protected override void SetActions() =>
    Actions.AddAgentModelActions(db, L, synchronizer, () => ParentRecord<AppAgentProvider>()?.Handle);
}
