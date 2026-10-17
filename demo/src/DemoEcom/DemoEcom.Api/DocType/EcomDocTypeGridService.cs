namespace DemoEcom.DocType;

public sealed class EcomDocTypeGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocType>(db, ecom)
{
  public override Feature Feature => Ecom.DocType;

  protected override void SetColumns(Columns<EcomDocType> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Handle);
    columns.Add(r => r.Name);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
