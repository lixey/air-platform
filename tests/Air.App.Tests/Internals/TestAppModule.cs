namespace Platform.App.Tests.Internals;

[Module("Test")]
internal sealed class TestAppModule : ModuleDefinition<TestAppModule>
{
  public static readonly Feature Invoices = Feature<TestInvoiceGridService>();
  public static readonly Feature SiblingInvoices = Feature<TestSiblingInvoiceFormService>();
  public static readonly Feature DirectLines = Feature<TestDirectBoundChildGridService>();
  public static readonly Right ApproveInvoice = Invoices.Action("Approve");
  public static readonly Feature Tags = Feature<TestTagGridService>();
  public static readonly Feature Changes = Feature<TestChangeGridService>().ReadOnly();
  public static readonly Feature SummaryReport = Feature<TestSummaryReportService>().ReadOnly().WithoutExport();
  public static readonly Feature OverviewScreen = Feature<TestOverviewScreenService>().ReadOnly().WithoutExport();
}
