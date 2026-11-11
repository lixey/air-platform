namespace DemoEcom.SalesWorkspace;

public sealed class EcomSalesWorkspaceScreenService(EcomService? ecom = null) : ScreenService(ecom)
{
  public override Feature Feature => Ecom.SalesWorkspace;

  public override Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    Task.FromResult(new ScreenDto
    {
      Layout = PanelLayout.Row(servicePath)
        .Screen<EcomCustomerOrderGridService>("orders", p => p
          .Label(L["Customer Orders"])
          .Fill()
          .Min(520))
        .Screen<EcomStockBalanceGridService>("stock", p => p
          .Label(L["Stock Balances"])
          .Size(480)
          .Min(320)
          .Max(720)
          .Resizable()
          .Collapsible())
        .Build()
    });
}
