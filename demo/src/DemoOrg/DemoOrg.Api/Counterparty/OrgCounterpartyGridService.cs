namespace DemoOrg.Counterparty;

public sealed class OrgCounterpartyGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCounterparty>(db, org)
{
  public override Feature Feature => Org.Counterparty;

  protected override void SetColumns(Columns<OrgCounterparty> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgCounterpartyFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.TaxId);
    columns.Add(r => r.IsSupplier).AsCheckbox();
    columns.Add(r => r.IsBuyer).AsCheckbox();
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.PaymentDelayDays);
    columns.Add(r => r.DeliveryDays);
    columns.Add(r => r.Phone).AsPhone();
    columns.Add(r => r.Email).AsEmail();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);

  protected override void SetHooks(Hooks hooks)
  {
    var inUse = L["The counterparty supplies products and cannot be deleted."];
    hooks.ValidateDelete(async (c, cancellationToken) =>
    {
      var selected = c.Records.Select(r => r.Id).ToList();
      var used = await db.OrgProduct.Where(product => selected.Contains(product.SupplierId))
        .Select(product => product.SupplierId).Distinct().ToListAsync(cancellationToken);
      foreach (var id in used)
      {
        c.AddError("counterparty.used", inUse, id);
      }
    });
  }
}

