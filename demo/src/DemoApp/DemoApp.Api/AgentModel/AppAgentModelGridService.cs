namespace DemoApp.AgentModel;

public sealed class AppAgentModelGridService(
  AppDbContext db,
  AppService? app = null,
  IAgentModelSynchronizer? synchronizer = null)
  : AppGridService<AppAgentModel>(db, app)
{
  public override Feature Feature => App.AgentModel;

  protected override void SetColumns() =>
    Columns.AddAgentModelColumns<AppAgentModelFormService>(L);

  protected override void SetQuery() =>
    Query.AddAgentModelQuery(db, ParentId, L);

  protected override void SetActions() =>
    Actions.AddAgentModelActions(db, L, synchronizer, () => ParentRecord<AppAgentProvider>()?.Handle);
}
