using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;

namespace DemoEcom.Documents;

public abstract class EcomDocLineFormService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDocLine>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => base.IsReadOnly || (ParentRecord<EcomDoc>() is { DocStatus: not null } doc && !doc.DocStatus.IsEditable);

  protected override string Title => "Line";

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(l => l.DocId);

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad(c => EcomDocLines.Load(c.Entity, c.Input, c.IsNew));
    hooks.AfterSave(async (c, ct) =>
    {
      var parentDoc = ParentRecord<EcomDoc>();
      if (parentDoc != null)
      {
        await EcomDocTotals.ApplyAsync(db, parentDoc, ct);
      }
    });
  }
}
