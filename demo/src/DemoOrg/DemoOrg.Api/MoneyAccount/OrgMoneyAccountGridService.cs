namespace DemoOrg.MoneyAccount;

public sealed class OrgMoneyAccountGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgMoneyAccount>(db, org)
{
  public override Feature Feature => Org.MoneyAccount;

  protected override void SetColumns(Columns<OrgMoneyAccount> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgMoneyAccountFormService>();
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(a => a.Organization.Color.Handle);
    columns.Add(r => r.Name);
    columns.Add(r => r.IsCash).AsCheckbox();
    columns.Add(r => r.Iban);
    columns.Add(r => r.BankName);
    columns.Add(r => r.IsActive).AsCheckbox();
    columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}

