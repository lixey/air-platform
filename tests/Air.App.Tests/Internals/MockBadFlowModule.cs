namespace Platform.App.Tests.Internals;

[Module("MockBadFlow")]
internal sealed class MockBadFlowModule : ModuleDefinition<MockBadFlowModule>
{
  public static readonly Feature Orders = Feature<MockBadFlowGridService>();
}
