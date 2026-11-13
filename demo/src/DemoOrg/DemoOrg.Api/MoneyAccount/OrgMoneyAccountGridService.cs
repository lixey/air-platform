namespace DemoOrg.MoneyAccount;

public sealed class OrgMoneyAccountGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgMoneyAccount>(db, org)
{
  public override Feature Feature => Org.MoneyAccount;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgMoneyAccountFormService>();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(a => a.Organization.Color.Handle);
    Columns.Add(r => r.Name);
    Columns.Add(r => r.IsCash).AsCheckbox();
    Columns.Add(r => r.Iban);
    Columns.Add(r => r.BankName);
    Columns.Add(r => r.IsActive).AsCheckbox();
    Columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);
}

