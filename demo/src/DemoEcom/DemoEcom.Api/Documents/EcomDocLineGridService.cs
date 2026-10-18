using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;

namespace DemoEcom.Documents;

public abstract class EcomDocLineGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine, EcomDocLineRow>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => base.IsReadOnly || (ParentRecord<EcomDoc>() is { DocStatus: not null } doc && !doc.DocStatus.IsEditable);

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(l => l.DocId);

  protected override void SetQuery(Query query)
  {
    query.Select(EcomDocLines.Row);
    query.SortBy(r => r.SortOrder);
  }

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.ValidateDelete(ctx =>
    {
      if (ParentRecord<EcomDoc>() is { DocStatus: { IsEditable: false } })
      {
        ctx.Refuse(EcomDocTexts.Of(L).NotEditable);
      }
    });
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

