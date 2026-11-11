namespace Platform.App.Tests.Internals;

[Module("MockBadSetAction")]
internal sealed class MockBadSetActionModule : ModuleDefinition<MockBadSetActionModule>
{
  public static readonly Feature ReadOnlyItems = Feature<MockBadSetActionGridService>().ReadOnly();
}
