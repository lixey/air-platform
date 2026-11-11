namespace Platform.App.Tests.Internals;

internal sealed class MockParallelScreenModuleImpl : PlatformModule<MockParallelScreenModule>
{
  public override ModuleMenu Menu => ModuleMenu
    .Route(MockParallelScreenModule.Items)
    .Route(MockParallelScreenModule.Workspace)
    .Build();
}
