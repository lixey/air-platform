namespace DemoOrg.MoneyAccount;

public sealed class OrgMoneyAccountFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgMoneyAccount>(db, org)
{
  public override Feature Feature => Org.MoneyAccount;

  protected override void SetElements()
  {
    Elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew);
    Elements.Add(e => e.Name).Focus().Unique("The organization already has a money account of this name.", e => e.OrganizationId);
    Elements.Add(e => e.IsCash).AsCheckbox().Span(6);
    Elements.Add(e => e.Iban).Span(6);
    Elements.Add(e => e.BankName).Span(6);
    Elements.Add(e => e.IsActive).AsCheckbox().Span(6);
    Elements.Add(e => e.SortOrder).Span(6);
  }

  protected override void SetHooks()
  {
    Hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (c.IsNew)
      {
        var organization = c.Entity.OrganizationId;
        c.Entity.CurrencyId = await db.OrgOrganization.Where(o => o.Id == organization).Select(o => o.CurrencyId)
          .SingleAsync(cancellationToken);
      }
    });
  }
}

