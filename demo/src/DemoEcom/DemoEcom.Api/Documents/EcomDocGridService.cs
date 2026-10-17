namespace DemoEcom.Documents;

public abstract class EcomDocGridService(EcomDbContext db, EcomService? ecom = null, EcomDocFlowService? flow = null)
  : EcomGridService<EcomDoc>(db, ecom)
{
  protected abstract long Type { get; }
  protected virtual EcomDocFlowService? Flow => flow;

  protected virtual GridColumn<long> AddStatusColumn(Columns<EcomDoc> columns)
  {
    var col = columns.Add(r => r.DocStatusId).Label("Status");
    if (Flow != null)
    {
      col.AsSelect(Flow.Statuses);
    }
    return col;
  }

  protected override void SetQuery(Query query)
  {
    query.Where(doc => doc.DocTypeId == Type);
    query.SortByDescending(r => r.Date);
    query.RecordLabel(e => e.Number);
  }
}

