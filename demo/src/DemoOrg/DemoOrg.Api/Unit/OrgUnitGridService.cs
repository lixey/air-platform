namespace DemoOrg.Unit;

public sealed class OrgUnitGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgUnit>(db, org)
{
  public override Feature Feature => Org.Unit;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgUnitFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Handle);
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);
}
