namespace DemoEcom.StockBalance;

public sealed class EcomStockBalanceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockBalance, EcomStockBalanceRow>(db, ecom)
{
  public override Feature Feature => Ecom.StockBalance;

  protected override void SetQuery(Query query)
  {
    query.Select(balance => new EcomStockBalanceRow
    {
      Id = balance.Id,
      Organization = new Reference { Id = balance.Organization.Id, Name = balance.Organization.Name },
      Warehouse = new Reference { Id = balance.Warehouse.Id, Name = balance.Warehouse.Name },
      Product = new Reference { Id = balance.Product.Id, Name = balance.Product.Name },
      Article = balance.Product.Article,
      Qty = balance.Qty,
      Reserved = balance.Reserved,
      Expected = balance.Expected,
      Free = balance.Qty - balance.Reserved
    });

    query.SortBy(r => r.Article);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(balance => balance.Organization.Color.Handle);
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Article);
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Reserved).Sum();
    columns.Add(r => r.Expected).Sum();
    columns.Add(r => r.Free).Sum();
  }
}
