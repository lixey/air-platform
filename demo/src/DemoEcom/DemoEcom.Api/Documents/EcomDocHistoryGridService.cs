namespace DemoEcom.Documents;

public abstract class EcomDocHistoryGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockMove>(db, ecom)
  where TDocForm : class
{
  protected override void SetColumns(Columns<EcomStockMove> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Date);
    columns.Add(r => r.Qty).Sum();
  }

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(m => m.DocId);
}
