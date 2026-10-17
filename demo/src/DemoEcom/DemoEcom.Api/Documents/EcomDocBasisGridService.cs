using DemoEcom.Data.Models;

namespace DemoEcom.Documents;

/// <summary>
/// Abstract domain base for document basis ("Entered on") grids across Demo Ecom.
/// </summary>
public abstract class EcomDocBasisGridService<TForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLink, EcomDocLinkRow>(db, ecom)
  where TForm : class
{
  protected override bool IsReadOnly => true;

  /// <summary>A row carries the document the link comes from: the shared projection of a basis.</summary>
  protected override void SetQuery(Query query)
  {
    query.Where(EcomDocLinks.Documents);
    query.Select(EcomDocLinks.Basis);
  }

  protected override void SetParent(Parent parent) => parent.Bind<TForm>(l => l.TargetDocId);

  protected override void SetColumns(Columns<EcomDocLinkRow> columns)
  {
    columns.Add(r => r.Number);
    columns.Add(r => r.Type);
    columns.Add(r => r.Date);
    columns.Add(r => r.Status).Color(EcomDocLinks.BasisHue);
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
  }
}
