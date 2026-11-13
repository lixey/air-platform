namespace DemoOrg.DeliveryType;

public sealed class OrgDeliveryTypeFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgDeliveryType>(db, org)
{
  public override Feature Feature => Org.DeliveryType;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus().Unique("This name is already in use.");
    Elements.Add(e => e.IsPickup).AsCheckbox().Span(4);
    Elements.Add(e => e.IsActive).AsCheckbox().Span(4);
    Elements.Add(e => e.SortOrder).Span(4);
  }
}

