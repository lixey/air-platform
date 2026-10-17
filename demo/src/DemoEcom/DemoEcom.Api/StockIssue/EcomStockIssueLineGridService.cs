using DemoEcom.Documents;

namespace DemoEcom.StockIssue;

public sealed class EcomStockIssueLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomStockIssueFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockIssue;

  protected override void SetColumns(Columns<EcomDocLineRow> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockIssueLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    columns.Add(r => r.Price).AsMoney().Editable();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
