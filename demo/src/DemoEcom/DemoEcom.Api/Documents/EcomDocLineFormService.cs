namespace DemoEcom.Documents;

public abstract class EcomDocLineFormService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDocLine>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => base.IsReadOnly || ParentRecord<EcomDoc>() is not { DocStatus.IsEditable: true };

  protected override string Title => "Line";

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(l => l.DocId);

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad((c, ct) => EcomDocLines.LoadAsync(db, ParentRecord<EcomDoc>(), c.Entity, c.IsNew, ct));
    hooks.Compute(c =>
      c.Entity.Amount = EcomDocTotals.LineAmount(
        c.Entity.Qty,
        c.Entity.Price,
        c.Entity.DiscountPercent));
  }
}
