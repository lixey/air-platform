using System.Linq.Expressions;
using Air.App.Components;
using DemoEcom.Data.Models;

namespace DemoEcom.Documents;

public static class EcomDocLines
{
  public static Expression<Func<EcomDocLine, EcomDocLineRow>> Row { get; } = line => new EcomDocLineRow
  {
    Id = line.Id,
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
}
