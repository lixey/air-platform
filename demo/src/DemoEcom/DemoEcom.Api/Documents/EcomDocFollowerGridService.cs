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
  protected override void SetQuery(Query query)
  {
    query.Where(EcomDocLinks.Documents);
  }

  protected override void SetParent(Parent parent) => parent.Bind<TForm>(l => l.SourceDocId);

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.TargetDoc.Number);
    columns.Add(r => r.TargetDoc.DocType.Name).Label("Type");
    columns.Add(r => r.TargetDoc.Date);
    columns.Add(r => r.TargetDoc.DocStatus.Name).Label("Status").Color(EcomDocLinks.FollowerHue);
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
  }
}
