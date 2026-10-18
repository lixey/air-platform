namespace DemoEcom.DocType;

/// <summary>The document types, read only: each with its handle and the prefix of its documents' numbers.</summary>
public sealed class EcomDocTypeGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocType>(db, ecom)
{
  public override Feature Feature => Ecom.DocType;

  protected override void SetQuery(Query query)
  {
    query.SortBy(r => r.SortOrder);
    query.RecordLabel(e => e.Name);
  }

  protected override void SetColumns(Columns<EcomDocType> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomDocTypeFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Handle);
    columns.Add(r => r.Prefix);
  }
}
