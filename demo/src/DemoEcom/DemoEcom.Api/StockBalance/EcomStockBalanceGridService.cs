namespace DemoEcom.StockBalance;

public sealed class EcomStockBalanceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockBalance>(db, ecom)
{
  public override Feature Feature => Ecom.StockBalance;

  protected override void SetQuery()
  {
    Query.SortBy(r => r.Product.Article);
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(balance => balance.Organization.Color.Handle);
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.Reserved).Sum();
    Columns.Add(r => r.Expected).Sum();
    Columns.Add("free", r => r.Qty - r.Reserved).Sum();
  }
}
