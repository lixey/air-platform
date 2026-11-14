namespace DemoApp.AgentMemory;

public sealed class AppAgentMemoryGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAgentMemory>(db, app)
{
  public override Feature Feature => App.AgentMemory;

  protected override void SetColumns() =>
    Columns.AddAgentMemoryColumns<AppAgentMemoryFormService>(L);

  protected override void SetQuery() =>
    Query.AddAgentMemoryQuery(L);

  protected override void SetActions() =>
    Actions.AddAgentMemoryActions(Time, L);
}
