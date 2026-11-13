namespace DemoOrg.PaymentTerm;

public sealed class OrgPaymentTermFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgPaymentTerm>(db, org)
{
  public override Feature Feature => Org.PaymentTerm;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.Handle);
  }
}
