namespace DemoOrg.Unit;

public sealed class OrgUnitFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgUnit>(db, org)
{
  public override Feature Feature => Org.Unit;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.Handle);
  }
}
