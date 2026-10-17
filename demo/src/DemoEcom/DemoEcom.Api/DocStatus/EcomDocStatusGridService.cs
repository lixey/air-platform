namespace DemoEcom.DocStatus;

public sealed class EcomDocStatusGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocStatus>(db, ecom)
{
  public override Feature Feature => Ecom.DocStatus;

  protected override void SetColumns(Columns<EcomDocStatus> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.DocTypeId);
    columns.Add(r => r.Handle);
    columns.Add(r => r.Name);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
