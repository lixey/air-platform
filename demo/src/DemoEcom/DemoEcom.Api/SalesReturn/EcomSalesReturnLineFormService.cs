namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomSalesReturnFormService>(db, ecom)
{
  private const string ProductPath = "productId";
  private const string QtyPath = "qty";

  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetElements()
  {
    Elements.Add(e => e.Product)
      .AsReference<OrgProductGridService>()
      .PatchOnSelect(e => e.Price, p => p.Price)
      .Required()
      .Span(12);
    Elements.Add(e => e.Qty).Label("Quantity").Min(0.001m).Span(3);
    Elements.Add(e => e.Price).AsMoney().Min(0m).Placeholder("The product's price").Span(3);
    Elements.Add(e => e.DiscountPercent).Label("Discount").AsPercent().Range(0, 100)
      .Placeholder("The customer's discount").Span(3);
    Elements.Add(e => e.Amount).AsMoney().ServerOwned().Span(3);
    Elements.Add(e => e.Note).AsTextarea().Span(12);
  }

  protected override void SetHooks()
  {
    base.SetHooks();
    Hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (await EcomDocEntering.LineAsync(db, c.Entity, cancellationToken) is { } code)
      {
        c.AddError(code, EcomDocTexts.Of(L).Line(code), code == EcomDocRefusal.SourceProduct ? ProductPath : QtyPath);
        return;
      }

      var doc = ParentRecord<EcomDoc>();
      if (doc != null)
      {
        await EcomDocLines.PriceAsync(db, doc, c, product => product.Price, cancellationToken);
      }
      else
      {
        c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price * (1m - (c.Entity.DiscountPercent / 100m)), 2);
      }
    });
  }
}

