namespace DemoOrg.PaymentTerm;

public sealed class OrgPaymentTermGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgPaymentTerm>(db, org)
{
  public override Feature Feature => Org.PaymentTerm;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgPaymentTermFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Handle);
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);
}
