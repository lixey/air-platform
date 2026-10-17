using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp.Tenant;

public sealed class AppTenantGridService(DemoDbContext db) : GridService<AppTenant, AppTenant>(db)
{
  public override Feature Feature => App.Tenant;
  protected override string FormServicePath => "app/tenantform";

  protected override void SetColumns(Columns<AppTenant> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<AppTenantFormService>();
    columns.Add(r => r.Name).Label("Name");
    columns.Add(r => r.Handle).Label("Handle");
    columns.Add(r => r.TimeZone).Label("Time Zone");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class AppTenantFormService(DemoDbContext db) : FormService<AppTenant>(db)
{
  public override Feature Feature => App.Tenant;
  protected override string Title => "Tenant";

  protected override void SetElements(Elements<AppTenant> elements)
  {
    elements.Add(e => e.Name).Label("Tenant Name").Span(6);
    elements.Add(e => e.Handle).Label("Handle").Span(6);
    elements.Add(e => e.TimeZone).Label("Time Zone").Span(6);
    elements.Add(e => e.IsActive).Label("Active").AsCheckbox().Span(6);
  }
}
