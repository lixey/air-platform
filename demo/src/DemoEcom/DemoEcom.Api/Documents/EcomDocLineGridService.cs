namespace DemoEcom.Documents;

public abstract class EcomDocLineGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine>(db, ecom)
  where TDocForm : class
{
  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(l => l.DocId);

  protected override void SetQuery(Query query) => query.SortBy(l => l.SortOrder);
}
