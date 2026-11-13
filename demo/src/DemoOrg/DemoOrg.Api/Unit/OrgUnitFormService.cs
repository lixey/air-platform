namespace DemoOrg.Unit;

public sealed class OrgUnitFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgUnit>(db, org)
{
  public override Feature Feature => Org.Unit;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.Handle);
  }
}
