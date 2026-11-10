namespace Platform.App.Tests.Internals;

[Module("MockBadAction")]
internal sealed class MockBadActionModule : ModuleDefinition<MockBadActionModule>
{
  public static readonly Feature Items = Feature<MockBadActionGridService>();
}
