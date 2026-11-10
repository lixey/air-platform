namespace Platform.App.Tests.Internals;

internal sealed class MockBadFlowModuleImpl : PlatformModule<MockBadFlowModule>
{
  public override ModuleMenu Menu => ModuleMenu.Route(MockBadFlowModule.Orders).Build();
}
