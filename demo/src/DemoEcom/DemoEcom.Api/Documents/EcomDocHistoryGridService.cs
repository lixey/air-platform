namespace DemoEcom.Documents;

public abstract class EcomDocHistoryGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null, EcomDocFlowService? flow = null)
  : EcomGridService<EcomDocMove>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => true;

  protected override void SetQuery() => Query.SortByDescending(r => r.At).ThenByDescending(r => r.Id);

  protected override void SetColumns()
  {
    var statuses = flow?.Statuses ?? (db is null
      ? []
      : db.EcomDocStatus
        .AsNoTracking()
        .Include(s => s.Color)
        .OrderBy(s => s.SortOrder)
        .ToList()
        .Select(s => new OptionDto { Id = s.Id, Name = L[s.Name], Color = s.Color?.Handle.ToLowerInvariant() })
        .ToList());

    Columns.Add(r => r.At).Label("Time");
    Columns.Add(r => r.FromId).Label("From status").AsSelect(statuses);
    Columns.Add(r => r.ToId).Label("To status").AsSelect(statuses);
    Columns.Add(r => r.User).AsReference<AppUserGridService>();
    Columns.Add(r => r.Note).Label("Note");
  }

  protected override void SetParent() => Parent.Bind<TDocForm>(m => m.DocId);
}

