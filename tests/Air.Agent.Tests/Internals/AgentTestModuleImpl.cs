namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestModuleImpl : PlatformModule<AgentTestModule>
{
  public override ModuleMenu Menu => ModuleMenu.Route(AgentTestModule.Receipts).Build();
}
