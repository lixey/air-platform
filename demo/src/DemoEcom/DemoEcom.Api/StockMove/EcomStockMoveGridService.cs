namespace DemoEcom.StockMove;

public sealed class EcomStockMoveGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockMove>(db, ecom)
{
  public override Feature Feature => Ecom.StockMove;

  protected override void SetQuery(Query query)
  {
    query.SortByDescending(r => r.Date).ThenByDescending(r => r.Id);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Date);
    columns.Add(r => r.Doc.Number);
    columns.Add(r => r.Doc.DocType.Name).Label("Type");
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(move => move.Organization.Color.Handle);
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Product.Article);
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Reserved);
    columns.Add(r => r.Expected);
  }
}
