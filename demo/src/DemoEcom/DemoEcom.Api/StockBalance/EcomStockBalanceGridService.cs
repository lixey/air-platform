namespace DemoEcom.StockBalance;

public sealed class EcomStockBalanceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockBalance>(db, ecom)
{
  public override Feature Feature => Ecom.StockBalance;

  protected override void SetQuery(Query query)
  {
    query.SortBy(r => r.Product.Article);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(balance => balance.Organization.Color.Handle);
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Product.Article);
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Reserved).Sum();
    columns.Add(r => r.Expected).Sum();
    columns.Add("free", r => r.Qty - r.Reserved).Sum();
  }
}
