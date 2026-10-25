namespace DemoOrg.Currency;

public sealed class OrgCurrencyFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCurrency>(db, org)
{
  public override Feature Feature => Org.Currency;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Handle);
    elements.Add(e => e.Code);
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.Symbol);
  }
}
