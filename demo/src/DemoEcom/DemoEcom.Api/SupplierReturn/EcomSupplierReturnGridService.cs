namespace DemoEcom.SupplierReturn;

/// <summary>
/// The active tenant's supplier returns, the newest first.
/// </summary>
public sealed class EcomSupplierReturnGridService(
  EcomDbContext db, EcomSupplierReturnFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomDocTypeData.SupplierReturnId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.SupplierReturn;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomSupplierReturnFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
  }
}
