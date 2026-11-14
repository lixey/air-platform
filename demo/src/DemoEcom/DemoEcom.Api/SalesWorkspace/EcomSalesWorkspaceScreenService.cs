namespace DemoEcom.SalesWorkspace;

public sealed class EcomSalesWorkspaceScreenService(EcomService? ecom = null) : ScreenService(ecom)
{
  public override Feature Feature => Ecom.SalesWorkspace;

  protected override void SetPanels()
  {
    Panels
      .Screen<EcomCustomerOrderGridService>("orders", p => p
        .Label("Customer Orders")
        .Fill()
        .Min(520))
      .Screen<EcomStockBalanceGridService>("stock", p => p
        .Label("Stock Balances")
        .Size(480)
        .Min(320)
        .Max(720)
        .Resizable()
        .Collapsible());
  }
}
