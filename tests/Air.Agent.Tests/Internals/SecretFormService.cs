namespace Platform.Agent.Tests.Internals;

internal sealed class SecretFormService(AgentTestDbContext db, PlatformAppService app) : FormService<AgentTestSecret>(db, app)
{
  public override Feature Feature => AgentTestModule.Secrets;
  protected override void SetElements() => Elements.Add(s => s.Code).Required();
}
