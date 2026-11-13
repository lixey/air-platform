namespace DemoEcom.Documents;

/// <summary>
/// Abstract domain base for document follower ("Followed by") grids across Demo Ecom.
/// </summary>
public abstract class EcomDocFollowerGridService<TForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLink>(db, ecom)
  where TForm : class
{
  protected override bool IsReadOnly => true;

  /// <summary>A row carries the document the link targets: the shared projection of a follower.</summary>
  protected override void SetQuery()
  {
    Query.Where(EcomDocLinks.Documents);
  }

  protected override void SetParent() => Parent.Bind<TForm>(l => l.SourceDocId);

  protected override void SetColumns()
  {
    Columns.Add(r => r.TargetDoc.Number);
    Columns.Add(r => r.TargetDoc.DocType.Name).Label("Type");
    Columns.Add(r => r.TargetDoc.Date);
    Columns.Add(r => r.TargetDoc.DocStatus.Name).Label("Status").Color(EcomDocLinks.FollowerHue);
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
  }
}
