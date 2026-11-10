namespace Platform.App.Tests.Internals;

internal sealed class MockBadActionModuleImpl : PlatformModule<MockBadActionModule>
{
  public override ModuleMenu Menu => ModuleMenu.Route(MockBadActionModule.Items).Build();
}
