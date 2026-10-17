namespace DemoOrg.Unit;

public sealed class OrgUnitGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgUnit>(db, org)
{
  public override Feature Feature => Org.Unit;

  protected override void SetColumns(Columns<OrgUnit> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgUnitFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Handle);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
