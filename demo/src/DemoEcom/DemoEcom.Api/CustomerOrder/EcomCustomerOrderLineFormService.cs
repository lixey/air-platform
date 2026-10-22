namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.ProductId)
      .AsReference<OrgProductGridService>()
      .PatchOnSelect<OrgProduct>(e => e.Price, p => p.Price)
      .Required()
      .Span(12);
    elements.Add(e => e.Qty).Label("Quantity").Min(0.001m).Step(1).Stepper().Span(3);
    elements.Add(e => e.Price).InputOnly().AsMoney().Min(0m).Placeholder("The product's price").Span(3);
    elements.Add(e => e.DiscountPercent).Label("Discount").InputOnly().AsPercent().Range(0, 100)
      .Placeholder("The customer's discount").Span(3);
    elements.Add(e => e.Amount).AsMoney().ServerOwned().Span(3);
    elements.Add(e => e.Note).AsTextarea().Span(12);
  }

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.BeforeSave((c, ct) =>
    {
      var doc = ParentRecord<EcomDoc>();
      if (doc != null)
      {
        return EcomDocLines.PriceAsync(db, doc, c.Entity, c.Input, c.IsNew, product => product.Price, ct);
      }

      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price * (1m - (c.Entity.DiscountPercent / 100m)), 2);
      return Task.CompletedTask;
    });
  }
}
