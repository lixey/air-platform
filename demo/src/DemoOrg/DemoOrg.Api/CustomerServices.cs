using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using DemoOrg.Data;
using DemoOrg.Data.Models;

namespace DemoOrg.Customer;

public sealed class OrgCustomerGridService(OrgDbContext db) : GridService<OrgCustomer, OrgCustomer>(db)
{
  public override Feature Feature => Org.Customer;
  protected override string FormServicePath => "org/customerform";

  protected override void SetColumns(Columns<OrgCustomer> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<OrgCustomerFormService>();
    columns.Add(r => r.Code).Label("Code");
    columns.Add(r => r.Name).Label("Customer Name");
    columns.Add(r => r.Email).Label("Email");
    columns.Add(r => r.Phone).Label("Phone");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class OrgCustomerFormService(OrgDbContext db) : FormService<OrgCustomer>(db)
{
  public override Feature Feature => Org.Customer;
  protected override string Title => "Customer";

  protected override void SetElements(Elements<OrgCustomer> elements)
  {
    elements.Add(e => e.Code).Label("Customer Code").Span(4);
    elements.Add(e => e.Name).Label("Customer Name").Span(8);
    elements.Add(e => e.Email).Label("Email Address").Span(6);
    elements.Add(e => e.Phone).Label("Phone Number").Span(6);
    elements.Add(e => e.Address).Label("Address").Span(12);
    elements.Add(e => e.IsActive).Label("Active").AsCheckbox().Span(12);
  }
}
