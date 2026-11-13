namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetElements()
  {
    Elements.Add(e => e.Product)
      .AsReference<OrgProductGridService>()
      .PatchOnSelect(e => e.Price, p => p.Price)
      .Required()
      .Span(12);
    Elements.Add(e => e.Qty).Label("Quantity").Min(0.001m).Step(1).Stepper().Span(3);
    Elements.Add(e => e.Price).AsMoney().Min(0m).Placeholder("The product's price").Span(3);
    Elements.Add(e => e.DiscountPercent).Label("Discount").AsPercent().Range(0, 100)
      .Placeholder("The customer's discount").Span(3);
    Elements.Add(e => e.Amount).AsMoney().ServerOwned().Span(3);
    Elements.Add(e => e.Note).AsTextarea().Span(12);
  }

  protected override void SetRules() =>
    Rules.For(e => e.DiscountPercent)
      .Must(d => d <= 25m)
      .WithMessage(L["Discount exceeds 25% — verify manager approval."])
      .AsWarning();

  protected override void SetHooks()
  {
    base.SetHooks();
    Hooks.BeforeSave((c, ct) =>
    {
      var doc = ParentRecord<EcomDoc>();
      if (doc != null)
      {
        return EcomDocLines.PriceAsync(db, doc, c, product => product.Price, ct);
      }

      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price * (1m - (c.Entity.DiscountPercent / 100m)), 2);
      return Task.CompletedTask;
    });
  }
}
