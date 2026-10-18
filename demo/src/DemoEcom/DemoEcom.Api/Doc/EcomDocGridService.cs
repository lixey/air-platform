using DemoEcom.DocStatus;
using DemoEcom.DocType;
using DemoOrg.Counterparty;

namespace DemoEcom.Doc;

public sealed class EcomDocGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDoc>(db, ecom)
{
  public override Feature Feature => Ecom.Doc;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Number).Width(180);
    columns.Add(r => r.Date);
    columns.Add(r => r.DocType).Label("Type").AsReference<EcomDocTypeGridService>();
    columns.Add(r => r.DocStatus).Label("Status").AsReference<EcomDocStatusGridService>().Color(doc => doc.DocStatus.Color.Handle);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Counterparty).AsReference<OrgCounterpartyGridService>();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetQuery(Query query)
  {
    query.SortByDescending(r => r.Date);
    query.RecordLabel(e => e.Number);
  }
}
