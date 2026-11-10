namespace Platform.App.Tests.Internals;

internal sealed class MockHorizModuleImpl : PlatformModule<MockHorizModule>
{
  public override ModuleMenu Menu => ModuleMenu.Route(MockHorizModule.Items).Build();
}
