namespace DemoEcom.Documents;

/// <summary>
/// Abstract domain base for document basis ("Entered on") grids across Demo Ecom.
/// </summary>
public abstract class EcomDocBasisGridService<TForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLink>(db, ecom)
  where TForm : class
{
  protected override bool IsReadOnly => true;

  /// <summary>A row carries the document the link comes from: the shared projection of a basis.</summary>
  protected override void SetQuery()
  {
    Query.Where(EcomDocLinks.Documents);
  }

  protected override void SetParent() => Parent.Bind<TForm>(l => l.TargetDocId);

  protected override void SetColumns()
  {
    Columns.Add(r => r.SourceDoc.Number);
    Columns.Add(r => r.SourceDoc.DocType.Name).Label("Type");
    Columns.Add(r => r.SourceDoc.Date);
    Columns.Add(r => r.SourceDoc.DocStatus.Name).Label("Status").Color(EcomDocLinks.BasisHue);
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
  }
}
