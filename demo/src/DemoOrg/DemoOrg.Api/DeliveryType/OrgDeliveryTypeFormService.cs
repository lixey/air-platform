namespace DemoOrg.DeliveryType;

public sealed class OrgDeliveryTypeFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgDeliveryType>(db, org)
{
  public override Feature Feature => Org.DeliveryType;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Unique("This name is already in use.");
    elements.Add(e => e.IsPickup).AsCheckbox().Span(4);
    elements.Add(e => e.IsActive).AsCheckbox().Span(4);
    elements.Add(e => e.SortOrder).Span(4);
  }
}

