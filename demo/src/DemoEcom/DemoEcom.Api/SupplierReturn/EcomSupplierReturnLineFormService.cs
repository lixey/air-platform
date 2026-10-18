namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomSupplierReturnFormService>(db, ecom)
{
  private const string ProductPath = "productId";
  private const string QtyPath = "qty";
  private EcomDocTexts? _texts;

  public override Feature Feature => Ecom.SupplierReturn;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.ProductId)
      .AsReference<OrgProductGridService>()
      .PatchOnSelect<OrgProduct>(e => e.Price, p => p.PurchasePrice)
      .Required()
      .Span(12);
    elements.Add(e => e.Qty).Label("Quantity").Min(0.001m).Span(4);
    elements.Add(e => e.Price).InputOnly().AsMoney().Min(0m).Placeholder("The product's purchase price").Span(4);
    elements.Add(e => e.Amount).AsMoney().ServerOwned().Span(4);
    elements.Add(e => e.Note).AsTextarea().Span(12);
  }

  protected override void SetRules(Rules rules)
  {
    _texts = EcomDocTexts.Of(L);
    rules.For(e => e.ProductId)
      .MustAsync((_, product, cancellationToken) =>
        EcomDocRules.ProductOfAsync(db, ParentRecord<EcomDoc>()?.CounterpartyId, product, cancellationToken))
      .WithMessage(_texts.ForeignProduct);
  }

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (await EcomDocEntering.LineAsync(db, c.Entity, cancellationToken) is { } code)
      {
        c.AddError(code, (_texts ?? EcomDocTexts.Of(L)).Line(code), code == EcomDocRefusal.SourceProduct ? ProductPath : QtyPath);
        return;
      }

      var doc = ParentRecord<EcomDoc>();
      if (doc != null)
      {
        await EcomDocLines.PriceAsync(db, doc, c.Entity, c.Input, c.IsNew, product => product.PurchasePrice, cancellationToken);
      }
      else
      {
        c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price, 2);
      }
    });
  }
}

