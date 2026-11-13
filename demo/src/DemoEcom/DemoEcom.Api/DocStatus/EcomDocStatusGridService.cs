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
  protected override void SetQuery()
  {
    Query.SortBy(r => r.Id);
    Query.RecordLabel(e => e.Name);
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomDocStatusFormService>();
    Columns.Add(r => r.DocType).Label("Type").AsReference<EcomDocTypeGridService>();
    Columns.Add(r => r.Name).Color(status => status.Color.Handle);
    Columns.Add(r => r.Handle);
    Columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(status => status.Color.Handle);
    Columns.Add(r => r.IsEditable).Label("Editable").AsCheckbox();
    Columns.Add(r => r.IsPosted).Label("Posted").AsCheckbox();
    Columns.Add(r => r.IsFinal).Label("Final").AsCheckbox();
    Columns.Add(r => r.IsCanceled).Label("Canceled").AsCheckbox();
    Columns.Add(r => r.SortOrder);
  }
}
