namespace DemoEcom.StockMove;

public sealed class EcomStockMoveGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockMove, EcomStockMoveRow>(db, ecom)
{
  public override Feature Feature => Ecom.StockMove;

  protected override void SetQuery(Query query)
  {
    query.Select(move => new EcomStockMoveRow
    {
      Id = move.Id,
      Date = move.Date,
      Number = move.Doc.Number,
      Type = move.Doc.DocType.Name,
      Organization = new Reference { Id = move.Organization.Id, Name = move.Organization.Name },
      Warehouse = new Reference { Id = move.Warehouse.Id, Name = move.Warehouse.Name },
      Product = new Reference { Id = move.Product.Id, Name = move.Product.Name },
      Article = move.Product.Article,
      Qty = move.Qty,
      Reserved = move.Reserved,
      Expected = move.Expected
    });

    query.SortByDescending(r => r.Date).ThenByDescending(r => r.Id);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Date);
    columns.Add(r => r.Number);
    columns.Add(r => r.Type);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(move => move.Organization.Color.Handle);
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Article);
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Reserved);
    columns.Add(r => r.Expected);
  }
}
