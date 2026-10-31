namespace Platform.Agent.Tests.Internals;

[Module("AgentTest")]
internal sealed class AgentTestModule : ModuleDefinition<AgentTestModule>
{
  public static readonly Feature Categories = Feature<CategoryGridService>().AllowAgent();
  public static readonly Feature Receipts = Feature<ReceiptGridService>().AllowAgent();
  public static readonly Feature Secrets = Feature<SecretGridService>();
}
