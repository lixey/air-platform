namespace Platform.App.Tests.Internals;

[Module("Test")]
internal sealed class TestAppModule : ModuleDefinition<TestAppModule>
{
  public static readonly Feature Invoices = Feature<TestInvoiceGridService>();
  public static readonly Feature Tags = Feature<TestTagGridService>();
  public static readonly Feature Changes = Feature<TestChangeGridService>().ReadOnly();
}
