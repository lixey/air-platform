namespace Platform.App.Tests.Internals;

internal sealed class TestAppModuleImpl : PlatformModule<TestAppModule>
{
  public override ModuleMenu Menu => ModuleMenu.Route(TestAppModule.Invoices).Build();
}
