using Air.App.Grid;
using Air.Data.Contracts;
using DemoEcom.Data;
using DemoEcom.Data.Documents;
using DemoOrg.Brand;
using DemoOrg.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderProductGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<OrgProduct, EcomCustomerOrderProductRow>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;
  protected override bool IsReadOnly => true;

  protected override void SetParent(Parent parent) => parent.Read<EcomCustomerOrderFormService>();

  protected override void SetColumns(Columns<EcomCustomerOrderProductRow> columns)
  {
    columns.Add(r => r.Article);
    columns.Add(r => r.Name);
    columns.Add(r => r.Brand).AsReference<OrgBrandGridService>();
    columns.Add(r => r.Price).AsMoney("UAH");
    columns.Add(r => r.InOrder);
    columns.Add(r => r.Free).Computed();
  }

  protected override void SetQuery(Query query)
  {
    var order = ParentId;
    query.Where(product => product.IsActive);
    query.Select(product => new EcomCustomerOrderProductRow
    {
      Id = product.Id,
      Article = product.Article,
      Name = product.Name,
      Brand = product.Brand != null ? new Reference { Id = product.Brand.Id, Name = product.Brand.Name } : null,
      Price = product.Price,
      InOrder = db.EcomDocLine.Any(line => line.DocId == order && line.ProductId == product.Id)
    });
    query.SortBy(r => r.Article);
    query.PickBy(r => r.Article, ArticleKey);
    query.FlagRows("unpriced", product => product.Price == 0);
    query.Compute(FillFreeAsync);
  }

  private async Task FillFreeAsync(IReadOnlyList<EcomCustomerOrderProductRow> rows, CancellationToken cancellationToken)
  {
    var doc = ParentRecord<EcomDoc>();
    if (doc is not { WarehouseId: { } warehouse, OrganizationId: var organization })
    {
      return;
    }

    var products = rows.Select(row => row.Id).ToList();
    var balances = await db.EcomStockBalance.AsNoTracking()
      .Where(balance => balance.OrganizationId == organization && balance.WarehouseId == warehouse && products.Contains(balance.ProductId))
      .Select(balance => new { balance.ProductId, balance.Qty, balance.Reserved })
      .ToListAsync(cancellationToken);

    var free = balances.ToLookup(balance => balance.ProductId, balance => new EcomStockQty(balance.Qty, balance.Reserved, 0m).Free);
    foreach (var row in rows)
    {
      row.Free = free[row.Id].Sum();
    }
  }

  private static string ArticleKey(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
