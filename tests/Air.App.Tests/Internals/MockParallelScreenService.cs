namespace Platform.App.Tests.Internals;

internal sealed class MockParallelScreenService(PlatformAppService? app = null) : ScreenService(app)
{
  public override Feature Feature => MockParallelScreenModule.Workspace;

  protected override void SetPanels() =>
    Panels.Screen<TestInvoiceGridService>("illegal");
}
