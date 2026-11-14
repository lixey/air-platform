namespace Platform.App.Tests.Internals;

[Module("MockBadDualScreen")]
internal sealed class MockBadDualScreenModule : ModuleDefinition<MockBadDualScreenModule>
{
  public static readonly Feature Overview = Feature<MockBadDualScreenService>().ReadOnly();
}
