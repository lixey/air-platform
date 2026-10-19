namespace DemoOrg.PaymentTerm;

public sealed class OrgPaymentTermGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgPaymentTerm>(db, org)
{
  public override Feature Feature => Org.PaymentTerm;

  protected override void SetColumns(Columns<OrgPaymentTerm> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgPaymentTermFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Handle);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
