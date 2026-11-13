namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderProductGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<OrgProduct>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;
  protected override bool IsReadOnly => true;

  protected override void SetParent() => Parent.Read<EcomCustomerOrderFormService>();

  protected override void SetColumns()
  {
    var order = ParentId;
    Columns.Add(r => r.Article);
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Brand).AsReference<OrgBrandGridService>();
    Columns.Add(r => r.Price).AsMoney("UAH");
    Columns.Add("inOrder", r => db.EcomDocLine.Any(line => line.DocId == order && line.ProductId == r.Id));
    Columns.Add<decimal>("free").Computed();
  }

  protected override void SetQuery()
  {
    Query.Where(product => product.IsActive);
    Query.SortBy(r => r.Article);
    Query.PickBy(r => r.Article, ArticleKey);
    Query.FlagRows("unpriced", product => product.Price == 0);
    Query.Compute(FillFreeAsync);
  }

  private async Task FillFreeAsync(IReadOnlyList<GridComputedRow<OrgProduct>> rows, CancellationToken cancellationToken)
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
      row["free"] = free[row.Id].Sum();
    }
  }

  private static string ArticleKey(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
