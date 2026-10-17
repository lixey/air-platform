namespace DemoEcom.Documents;

public abstract class EcomDocGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDoc>(db, ecom)
{
  protected abstract long Type { get; }

  protected virtual GridColumn<long> AddStatusColumn(Columns<EcomDoc> columns) =>
    columns.Add(r => r.DocStatusId).Label("Status");

  protected override void SetQuery(Query query)
  {
    query.Where(doc => doc.DocTypeId == Type);
    query.SortByDescending(r => r.Date);
    query.RecordLabel(e => e.Number);
  }
}
