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
    hooks.Compute(c =>
      c.Entity.Amount = EcomDocTotals.LineAmount(
        c.Entity.Qty,
        c.Input.Price ?? c.Entity.Price,
        c.Input.DiscountPercent ?? c.Entity.DiscountPercent));
  }
}
