namespace DemoEcom.Doc;

public sealed class EcomDocGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDoc>(db, ecom)
{
  public override Feature Feature => Ecom.Doc;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id);
    Columns.Add(r => r.Number).OpenForm(r => EcomDocForms.Of(r.DocType.Id)).Width(180);
    Columns.Add(r => r.Date);
    Columns.Add(r => r.DocType).Label("Type").AsReference<EcomDocTypeGridService>();
    Columns.Add(r => r.DocStatus).Label("Status").AsReference<EcomDocStatusGridService>().Color(doc => doc.DocStatus.Color.Handle);
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    Columns.Add(r => r.Counterparty).AsReference<OrgCounterpartyGridService>();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetQuery()
  {
    Query.SortByDescending(r => r.Date);
    Query.RecordLabel(e => e.Number);
  }
}
