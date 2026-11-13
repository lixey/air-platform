namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptLineFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineFormService<EcomStockReceiptFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockReceipt;

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

  protected override void SetHooks()
  {
    base.SetHooks();
    Hooks.BeforeSave((c, cancellationToken) =>
    {
      var doc = ParentRecord<EcomDoc>();
      if (doc != null)
      {
        return EcomDocLines.PriceAsync(db, doc, c, product => product.PurchasePrice, cancellationToken);
      }

      c.Entity.Amount = Math.Round(c.Entity.Qty * c.Entity.Price, 2);
      return Task.CompletedTask;
    });
  }
}

