namespace Platform.App.Tests.Internals;

internal sealed class MockBadSetActionModuleImpl : PlatformModule<MockBadSetActionModule>
{
  public override ModuleMenu Menu => ModuleMenu.Route(MockBadSetActionModule.ReadOnlyItems).Build();
}
