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

  protected override void SetHooks(Hooks hooks)
  {
    var inUse = L["The delivery type is used by customers and cannot be deleted."];
    hooks.ValidateDelete(async (c, cancellationToken) =>
    {
      var selected = c.Records.Select(r => (long?)r.Id).ToList();
      var used = await db.OrgCustomer.Where(customer => selected.Contains(customer.DeliveryTypeId))
        .Select(customer => customer.DeliveryTypeId!.Value).Distinct().ToListAsync(cancellationToken);
      foreach (var id in used)
      {
        c.AddError("deliveryType.used", inUse, id);
      }
    });
  }
}

