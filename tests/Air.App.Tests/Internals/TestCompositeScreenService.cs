namespace Platform.App.Tests.Internals;

internal sealed class TestCompositeScreenService(PlatformAppService? app = null) : ScreenService(app)
{
  public override Feature Feature => TestAppModule.CompositeScreen;

  protected override void SetPanels()
  {
    Panels
      .Screen<TestCountedInvoiceGridService>("invoices", p => p.Label("Invoices").Fill())
      .Screen<TestSummaryReportService>("summary", p => p.Label("Summary").Size(420).Collapsible());
  }
}
