using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;

namespace DemoEcom.Documents;

public abstract class EcomDocLineGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine, EcomDocLineRow>(db, ecom)
  where TDocForm : class
{
  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(l => l.DocId);

  protected override void SetQuery(Query query)
  {
    query.Select(EcomDocLines.Row);
    query.SortBy(r => r.SortOrder);
  }

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.AfterDelete(async (ctx, ct) =>
    {
      var parentDoc = ParentRecord<EcomDoc>();
      if (parentDoc != null)
      {
        await EcomDocTotals.ApplyAsync(db, parentDoc, ct);
      }
    });
  }
}
