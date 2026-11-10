namespace Platform.App.Tests.Internals;

[Module("MockHoriz")]
internal sealed class MockHorizModule : ModuleDefinition<MockHorizModule>
{
  public static readonly Feature Items = Feature<MockHorizGridService>();
}
