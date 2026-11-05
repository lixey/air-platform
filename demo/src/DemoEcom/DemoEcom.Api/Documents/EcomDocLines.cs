namespace DemoEcom.Documents;

public static class EcomDocLines
{
  public static async Task LoadAsync(
    EcomDbContext db,
    EcomDoc? doc,
    EcomDocLine entity,
    bool isNew,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(entity);
    if (isNew && doc != null && entity.DiscountPercent == 0m)
    {
      entity.DiscountPercent = Math.Round(await CustomerDiscountAsync(db, doc, cancellationToken), 2, MidpointRounding.AwayFromZero);
    }
  }

  public static Task PriceAsync(
    EcomDbContext db,
    EcomDoc doc,
    FormSaveContext<EcomDocLine> context,
    Expression<Func<OrgProduct, decimal>> productPrice,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(context);
    return PriceAsync(
      db,
      doc,
      context.Entity,
      context.IsNew,
      productPrice,
      cancellationToken,
      context.HasInput(e => e.Price),
      context.HasInput(e => e.DiscountPercent));
  }

  public static async Task PriceAsync(
    EcomDbContext db,
    EcomDoc doc,
    EcomDocLine entity,
    bool isNew,
    Expression<Func<OrgProduct, decimal>> productPrice,
    CancellationToken cancellationToken,
    bool hasPrice = false,
    bool hasDiscount = false)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(entity);
    ArgumentNullException.ThrowIfNull(productPrice);

    var price = hasPrice
      ? entity.Price
      : await ProductPriceAsync(db, entity.ProductId, productPrice, cancellationToken);
    var discount = hasDiscount
      ? entity.DiscountPercent
      : await CustomerDiscountAsync(db, doc, cancellationToken);
    entity.Price = Math.Round(price, 2, MidpointRounding.AwayFromZero);
    entity.DiscountPercent = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
    entity.Amount = EcomDocTotals.LineAmount(entity.Qty, entity.Price, entity.DiscountPercent);

    if (isNew)
    {
      var docId = doc.Id;
      var highest = await db.EcomDocLine.Where(line => line.DocId == docId).MaxAsync(line => (int?)line.SortOrder, cancellationToken);
      entity.SortOrder = (highest ?? 0) + 10;
    }
  }

  private static Task<decimal> ProductPriceAsync(
    EcomDbContext db, long productId, Expression<Func<OrgProduct, decimal>> price, CancellationToken cancellationToken) =>
    db.OrgProduct.Where(product => product.Id == productId).Select(price).FirstOrDefaultAsync(cancellationToken);

  private static async Task<decimal> CustomerDiscountAsync(EcomDbContext db, EcomDoc doc, CancellationToken cancellationToken) =>
    doc.CustomerId is { } customerId
      ? await db.OrgCustomer.Where(customer => customer.Id == customerId).Select(customer => customer.DiscountPercent)
        .FirstOrDefaultAsync(cancellationToken)
      : 0m;
}
