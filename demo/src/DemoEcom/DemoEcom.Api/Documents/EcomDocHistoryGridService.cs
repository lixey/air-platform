using Air.App.Grid;
using DemoApp.User;
using DemoEcom.Data;

namespace DemoEcom.Documents;

public abstract class EcomDocHistoryGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null, EcomDocFlowService? flow = null)
  : EcomGridService<EcomDocMove>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => true;

  protected override void SetColumns(Columns<EcomDocMove> columns)
  {
    columns.Add(r => r.At).Label("Time");
    if (flow != null)
    {
      columns.Add(r => r.FromId).Label("From status").AsSelect(flow.Statuses);
      columns.Add(r => r.ToId).Label("To status").AsSelect(flow.Statuses);
    }
    else
    {
      columns.Add(r => r.FromId).Label("From status");
      columns.Add(r => r.ToId).Label("To status");
    }
    columns.Add(r => r.UserId).AsReference<AppUserGridService>().Label("User");
    columns.Add(r => r.Note).Label("Note");
  }

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(m => m.DocId);
}
