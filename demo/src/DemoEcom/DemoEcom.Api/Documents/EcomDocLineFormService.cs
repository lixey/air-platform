namespace DemoEcom.Documents;

public abstract class EcomDocLineFormService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDocLine, EcomDocLineEdit>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => base.IsReadOnly || ParentRecord<EcomDoc>() is not { DocStatus.IsEditable: true };

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
        await db.EcomDoc.Where(d => d.Id == parentDoc.Id)
          .ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Qty, parentDoc.Qty)
            .SetProperty(d => d.Amount, parentDoc.Amount), ct);
        db.Entry(parentDoc).State = EntityState.Unchanged;
      }
    });
    hooks.BeforeDelete(c =>
    {
      if (ParentRecord<EcomDoc>() is not { DocStatus.IsEditable: true })
      {
        c.Refuse(EcomDocTexts.Of(L).NotEditable);
      }
    });
    hooks.AfterDelete(async (c, ct) =>
    {
      var parentDoc = ParentRecord<EcomDoc>();
      if (parentDoc != null)
      {
        await EcomDocTotals.ApplyAsync(db, parentDoc, ct);
        await db.EcomDoc.Where(d => d.Id == parentDoc.Id)
          .ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Qty, parentDoc.Qty)
            .SetProperty(d => d.Amount, parentDoc.Amount), ct);
        db.Entry(parentDoc).State = EntityState.Unchanged;
      }
    });
  }
}
