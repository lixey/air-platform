using DemoEcom.Data.Models;

namespace DemoEcom.Documents;

/// <summary>
/// Abstract domain base for document follower ("Followed by") grids across Demo Ecom.
/// </summary>
public abstract class EcomDocFollowerGridService<TForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLink, EcomDocLinkRow>(db, ecom)
  where TForm : class
{
  protected override bool IsReadOnly => true;

  /// <summary>A row carries the document the link targets: the shared projection of a follower.</summary>
  protected override void SetQuery(Query query)
  {
    query.Where(EcomDocLinks.Documents);
    query.Select(EcomDocLinks.Follower);
  }

  protected override void SetParent(Parent parent) => parent.Bind<TForm>(l => l.SourceDocId);

  protected override void SetColumns(Columns<EcomDocLinkRow> columns)
  {
    columns.Add(r => r.Number);
    columns.Add(r => r.Type);
    columns.Add(r => r.Date);
    columns.Add(r => r.Status).Color(EcomDocLinks.FollowerHue);
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
  }
}
