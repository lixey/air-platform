namespace DemoEcom.Documents;

public abstract class EcomDocGridService(EcomDbContext db, EcomService? ecom = null, EcomDocFlowService? flow = null)
  : EcomGridService<EcomDoc>(db, ecom)
{
  protected abstract long Type { get; }
  protected virtual EcomDocFlowService? Flow => flow;

  protected virtual GridColumn<EcomDoc, EcomDoc, long> AddStatusColumn()
  {
    var col = Columns.Add(r => r.DocStatusId).Label("Status");
    if (Flow != null)
    {
      col.AsSelect(Flow.Statuses);
      Columns.UseStatusOf(Flow);
    }

    return col;
  }

  protected override void SetQuery()
  {
    Query.Where(doc => doc.DocTypeId == Type);
    Query.SortByDescending(r => r.Date);
    Query.RecordLabel(e => e.Number);
  }

  protected override void SetHooks() =>
    Hooks.BeforeDelete((c, cancellationToken) => EcomDocActions.RefuseDeleteAsync(
        db,
        c,
        Type,
        EcomDocTexts.Of(L),
        cancellationToken
      )
    );
}

