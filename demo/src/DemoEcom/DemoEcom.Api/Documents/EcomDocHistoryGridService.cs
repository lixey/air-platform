using Air.App.Components;
using Air.App.Grid;
using DemoApp.User;
using DemoEcom.Data;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Documents;

public abstract class EcomDocHistoryGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null, EcomDocFlowService? flow = null)
  : EcomGridService<EcomDocMove>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => true;

  protected override void SetQuery(Query query) => query.SortByDescending(r => r.At).ThenByDescending(r => r.Id);

  protected override void SetColumns(Columns<EcomDocMove> columns)
  {
    var statuses = flow?.Statuses ?? db.EcomDocStatus
      .AsNoTracking()
      .Include(s => s.Color)
      .OrderBy(s => s.SortOrder)
      .ToList()
      .Select(s => new OptionDto { Id = s.Id, Name = L[s.Name], Color = s.Color?.Handle.ToLowerInvariant() })
      .ToList();

    columns.Add(r => r.At).Label("Time");
    columns.Add(r => r.FromId).Label("From status").AsSelect(statuses);
    columns.Add(r => r.ToId).Label("To status").AsSelect(statuses);
    columns.Add(r => r.User).AsReference<AppUserGridService>();
    columns.Add(r => r.Note).Label("Note");
  }

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(m => m.DocId);
}

