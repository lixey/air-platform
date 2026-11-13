namespace DemoEcom.DocType;

/// <summary>The document types, read only: each with its handle and the prefix of its documents' numbers.</summary>
public sealed class EcomDocTypeGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocType>(db, ecom)
{
  public override Feature Feature => Ecom.DocType;

  protected override void SetQuery()
  {
    Query.SortBy(r => r.SortOrder);
    Query.RecordLabel(e => e.Name);
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomDocTypeFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Handle);
    Columns.Add(r => r.Prefix);
  }
}
