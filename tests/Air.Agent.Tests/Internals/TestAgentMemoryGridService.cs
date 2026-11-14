namespace Platform.Agent.Tests.Internals;

internal sealed class TestAgentMemoryGridService(AgentTestDbContext db, PlatformAppService app)
  : GridService<AppAgentMemory>(db, app)
{
  public override Feature Feature => AgentTestModule.AgentMemories;

  protected override void SetNav() => Nav.AddCreate<TestAgentMemoryFormService>();

  protected override void SetColumns() =>
    Columns.AddAgentMemoryColumns<TestAgentMemoryFormService>(L);

  protected override void SetQuery() =>
    Query.AddAgentMemoryQuery(L);

  protected override void SetActions() =>
    Actions.AddAgentMemoryActions(app.Time, L);
}
