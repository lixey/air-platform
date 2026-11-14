namespace Platform.App.Tests.Internals;

internal sealed class MockBadDualScreenService(PlatformAppService? app = null)
  : ScreenService(app)
{
  public override Feature Feature => MockBadDualScreenModule.Overview;

  protected override void SetPanels() =>
    Panels.Row().Screen<TestInvoiceGridService>("invoices");

  protected override Task SetPanelsAsync(CancellationToken cancellationToken = default)
  {
    Panels.Column().Screen<TestInvoiceGridService>("invoices");
    return Task.CompletedTask;
  }
}
