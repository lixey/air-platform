namespace Platform.Agent.Tests.Internals;

internal sealed class SecretGridService(AgentTestDbContext db, PlatformAppService app) : GridService<AgentTestSecret>(db, app)
{
  public override Feature Feature => AgentTestModule.Secrets;
  protected override void SetNav(Nav nav) => nav.AddCreate<SecretFormService>();
  protected override void SetColumns(Columns columns) => columns.Add(s => s.Code);
}
