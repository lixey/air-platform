namespace DemoOrg.MoneyAccount;

public sealed class OrgMoneyAccountGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgMoneyAccount>(db, org)
{
  public override Feature Feature => Org.MoneyAccount;

  protected override void SetColumns(Columns<OrgMoneyAccount> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgMoneyAccountFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(a => a.Organization.Color.Handle);
    columns.Add(r => r.Currency).AsReference<OrgCurrencyGridService>();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
