namespace DemoOrg.PaymentMethod;

public sealed class OrgPaymentMethodGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgPaymentMethod>(db, org)
{
  public override Feature Feature => Org.PaymentMethod;

  protected override void SetColumns(Columns<OrgPaymentMethod> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgPaymentMethodFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
