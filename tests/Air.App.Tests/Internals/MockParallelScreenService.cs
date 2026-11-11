namespace Platform.App.Tests.Internals;

internal sealed class MockParallelScreenService(PlatformAppService? app = null) : ScreenService(app)
{
  public override Feature Feature => MockParallelScreenModule.Workspace;

  public override Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    Task.FromResult(new ScreenDto
    {
      Layout = PanelLayout.Row(servicePath)
        .Screen<TestInvoiceGridService>("illegal")
        .Build()
    });
}
