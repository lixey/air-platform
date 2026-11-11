namespace Platform.App.Tests.Internals;

[Module("MockParallel")]
internal sealed class MockParallelScreenModule : ModuleDefinition<MockParallelScreenModule>
{
  public static readonly Feature Items = Feature<MockParallelScreenGridService>();
  public static readonly Feature Workspace = Feature<MockParallelScreenService>().ReadOnly().WithoutExport();
}
