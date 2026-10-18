using DemoApp.Color;
using DemoEcom.DocType;

namespace DemoEcom.DocStatus;

/// <summary>
/// The statuses of every document type, read only, in the order of their types and of a document's passage: each with its
/// type, its name painted in its hue, its handle, its color and its flags, editable, posted, final and canceled.
/// </summary>
public sealed class EcomDocStatusGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocStatus>(db, ecom)
{
  public override Feature Feature => Ecom.DocStatus;

  /// <summary>
  /// A status's id is its type's times a hundred plus its place: the ids run in the order of the types and of the passage.
  /// </summary>
  protected override void SetQuery(Query query)
  {
    query.SortBy(r => r.Id);
    query.RecordLabel(e => e.Name);
  }

  protected override void SetColumns(Columns<EcomDocStatus> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomDocStatusFormService>();
    columns.Add(r => r.DocType).Label("Type").AsReference<EcomDocTypeGridService>();
    columns.Add(r => r.Name).Color(status => status.Color.Handle);
    columns.Add(r => r.Handle);
    columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(status => status.Color.Handle);
    columns.Add(r => r.IsEditable).Label("Editable").AsCheckbox();
    columns.Add(r => r.IsPosted).Label("Posted").AsCheckbox();
    columns.Add(r => r.IsFinal).Label("Final").AsCheckbox();
    columns.Add(r => r.IsCanceled).Label("Canceled").AsCheckbox();
    columns.Add(r => r.SortOrder);
  }
}
