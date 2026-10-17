namespace DemoEcom.Documents;

public abstract class EcomDocLineFormService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDocLine>(db, ecom)
  where TDocForm : class
{
  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(l => l.DocId);

  protected override void SetHooks(Hooks hooks)
  {
    hooks.BeforeSave((c, ct) =>
    {
      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price * (1m - (c.Entity.DiscountPercent / 100m)), 2);
      return Task.CompletedTask;
    });
  }
}
