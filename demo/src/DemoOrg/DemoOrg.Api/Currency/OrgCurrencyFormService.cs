namespace DemoOrg.Currency;

public sealed class OrgCurrencyFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCurrency>(db, org)
{
  public override Feature Feature => Org.Currency;

  protected override void SetElements()
  {
    Elements.Add(e => e.Handle);
    Elements.Add(e => e.Code);
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.Symbol);
  }
}
