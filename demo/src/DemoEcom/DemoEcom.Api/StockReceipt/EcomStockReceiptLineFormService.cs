namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomStockReceiptFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockReceipt;

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

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.BeforeSave((c, cancellationToken) =>
    {
      var doc = ParentRecord<EcomDoc>();
      if (doc != null)
      {
        return EcomDocLines.PriceAsync(db, doc, c.Entity, c.Input, c.IsNew, product => product.PurchasePrice, cancellationToken);
      }
      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price, 2);
      return Task.CompletedTask;
    });
  }
}

