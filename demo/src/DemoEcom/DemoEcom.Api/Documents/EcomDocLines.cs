using System.Linq.Expressions;
using Air.App.Components;
using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;
using DemoOrg.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Documents;

public static class EcomDocLines
{
  public static Expression<Func<EcomDocLine, EcomDocLineRow>> Row { get; } = line => new EcomDocLineRow
  {
    Id = line.Id,
    Ver = line.Ver,
    SortOrder = line.SortOrder,
    Product = new Reference { Id = line.Product.Id, Name = line.Product.Name },
    Article = line.Product.Article,
    Qty = line.Qty,
    QtyReserved = line.QtyReserved,
    QtyDone = line.QtyDone,
    Price = line.Price,
    DiscountPercent = line.DiscountPercent,
    Amount = line.Amount
  };


  public static void Load(EcomDocLine entity, EcomDocLine input, bool isNew)
  {
    ArgumentNullException.ThrowIfNull(entity);
    ArgumentNullException.ThrowIfNull(input);
    input.Price = isNew ? 0 : entity.Price;
    input.DiscountPercent = isNew ? 0 : entity.DiscountPercent;
  }

  public static async Task PriceAsync(
    EcomDbContext db,
    EcomDoc doc,
    EcomDocLine entity,
    EcomDocLine input,
    bool isNew,
    Expression<Func<OrgProduct, decimal>> productPrice,
    CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(doc);
    ArgumentNullException.ThrowIfNull(entity);
    ArgumentNullException.ThrowIfNull(input);
    ArgumentNullException.ThrowIfNull(productPrice);

    var price = input.Price > 0 ? input.Price : await ProductPriceAsync(db, entity.ProductId, productPrice, cancellationToken);
    var discount = input.DiscountPercent > 0 ? input.DiscountPercent : await CustomerDiscountAsync(db, doc, cancellationToken);
    entity.Price = Math.Round(price, 2, MidpointRounding.AwayFromZero);
    entity.DiscountPercent = Math.Round(discount, 2, MidpointRounding.AwayFromZero);
    entity.Amount = EcomDocTotals.LineAmount(entity.Qty, entity.Price, entity.DiscountPercent);

    if (isNew)
    {
      var docId = doc.Id;
      var highest = await db.EcomDocLine.Where(line => line.DocId == docId).MaxAsync(line => (int?)line.SortOrder, cancellationToken);
      entity.SortOrder = (highest ?? 0) + 10;
    }

    input.Price = entity.Price;
    input.DiscountPercent = entity.DiscountPercent;
    input.Amount = entity.Amount;
    input.SortOrder = entity.SortOrder;
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
