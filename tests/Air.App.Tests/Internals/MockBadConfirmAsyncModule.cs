namespace Platform.App.Tests.Internals;

[Module("MockBadConfirmAsync")]
internal sealed class MockBadConfirmAsyncModule : ModuleDefinition<MockBadConfirmAsyncModule>
{
  public static readonly Feature Items = Feature<MockBadConfirmAsyncGridService>();
}
