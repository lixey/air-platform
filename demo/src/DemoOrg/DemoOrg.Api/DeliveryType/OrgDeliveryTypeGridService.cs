namespace DemoOrg.DeliveryType;

public sealed class OrgDeliveryTypeGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgDeliveryType>(db, org)
{
  public override Feature Feature => Org.DeliveryType;

  protected override void SetColumns(Columns<OrgDeliveryType> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgDeliveryTypeFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.IsPickup).AsCheckbox();
    columns.Add(r => r.IsActive).AsCheckbox();
    columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);

  protected override void SetHooks(Hooks hooks) =>
    hooks.PreventDeleteWhenUsedBy<OrgCustomer>(c => c.DeliveryTypeId, "The delivery type is used by customers and cannot be deleted.");
}

