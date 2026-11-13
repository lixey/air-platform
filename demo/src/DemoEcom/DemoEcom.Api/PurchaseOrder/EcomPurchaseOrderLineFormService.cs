namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomPurchaseOrderFormService>(db, ecom)
{
  private const string ProductPath = "productId";

  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetElements()
  {
    Elements.Add(e => e.Product)
      .AsReference<OrgProductGridService>()
      .PatchOnSelect(e => e.Price, p => p.PurchasePrice)
      .Required()
      .Span(12);
    Elements.Add(e => e.Qty).Label("Quantity").Min(0.001m).Span(4);
    Elements.Add(e => e.Price).AsMoney().Min(0m).Placeholder("The product's purchase price").Span(4);
    Elements.Add(e => e.Amount).AsMoney().ServerOwned().Span(4);
    Elements.Add(e => e.Note).AsTextarea().Span(12);
  }

  protected override void SetRules()
  {
    var texts = EcomDocTexts.Of(L);
    Rules.For(e => e.ProductId)
      .MustAsync((_, product, cancellationToken) =>
        EcomDocRules.ProductOfAsync(db, ParentRecord<EcomDoc>()?.CounterpartyId, product, cancellationToken))
      .WithMessage(texts.ForeignProduct);
  }

  protected override void SetHooks()
  {
    base.SetHooks();
    Hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (await EcomDocPurchasing.PurchaseLineAsync(db, c.Entity, cancellationToken) is { } code)
      {
        c.AddError(code, EcomDocTexts.Of(L).Line(code), ProductPath);
        return;
      }

      var doc = ParentRecord<EcomDoc>();
      if (doc != null)
      {
        await EcomDocLines.PriceAsync(db, doc, c, product => product.PurchasePrice, cancellationToken);
      }
      else
      {
        c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price, 2);
      }
    });
  }
}

