namespace DemoOrg.MoneyAccount;

public sealed class OrgMoneyAccountFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgMoneyAccount>(db, org)
{
  public override Feature Feature => Org.MoneyAccount;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>();
    elements.Add(e => e.CurrencyId).AsReference<OrgCurrencyGridService>();
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
