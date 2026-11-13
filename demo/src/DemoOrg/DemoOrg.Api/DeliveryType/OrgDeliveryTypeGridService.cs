namespace DemoOrg.DeliveryType;

public sealed class OrgDeliveryTypeGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgDeliveryType>(db, org)
{
  public override Feature Feature => Org.DeliveryType;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgDeliveryTypeFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.IsPickup).AsCheckbox();
    Columns.Add(r => r.IsActive).AsCheckbox();
    Columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);

  protected override void SetHooks() =>
    Hooks.PreventDeleteWhenUsedBy<OrgCustomer>(c => c.DeliveryTypeId, "The delivery type is used by customers and cannot be deleted.");
}

