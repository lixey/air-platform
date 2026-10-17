namespace DemoOrg.Currency;

public sealed class OrgCurrencyFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCurrency>(db, org)
{
  public override Feature Feature => Org.Currency;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Handle);
    elements.Add(e => e.Symbol);
  }
}
