namespace Platform.App.Tests.Internals;

internal sealed class TestCompositeScreenService(PlatformAppService? app = null) : ScreenService(app)
{
  public override Feature Feature => TestAppModule.CompositeScreen;

  public override Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    Task.FromResult(new ScreenDto
    {
      Layout = PanelLayout.Row(servicePath)
        .Screen<TestCountedInvoiceGridService>("invoices", p => p.Label("Invoices").Fill())
        .Screen<TestSummaryReportService>("summary", p => p.Label("Summary").Size(420).Collapsible())
        .Build()
    });
}
