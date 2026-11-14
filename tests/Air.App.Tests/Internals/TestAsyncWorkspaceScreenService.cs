namespace Platform.App.Tests.Internals;

internal sealed class TestAsyncWorkspaceScreenService(PlatformAppService? app = null)
  : ScreenService(app)
{
  public override Feature Feature => TestAppModule.CompositeScreen;

  protected override async Task SetPanelsAsync(CancellationToken cancellationToken = default)
  {
    await Task.Yield();
    Panels.Row()
      .Gap(12)
      .Screen<TestInvoiceGridService>("invoices", p => p.Fill().Label("Invoices"))
      .Screen<TestTagGridService>("tags", p => p.Size(320).Label("Tags"));
  }
}
