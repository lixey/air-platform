using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp.Role;

public sealed class AppRoleGridService(DemoDbContext db) : GridService<AppAuthRole, AppAuthRole>(db)
{
  public override Feature Feature => App.Role;
  protected override string FormServicePath => "app/roleform";

  protected override void SetColumns(Columns<AppAuthRole> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<AppRoleFormService>();
    columns.Add(r => r.Name).Label("Role Name");
    columns.Add(r => r.Handle).Label("Handle");
    columns.Add(r => r.Description).Label("Description");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class AppRoleFormService(DemoDbContext db) : FormService<AppAuthRole>(db)
{
  public override Feature Feature => App.Role;
  protected override string Title => "Role";

  protected override void SetElements(Elements<AppAuthRole> elements)
  {
    elements.Add(e => e.Name).Label("Role Name").Span(6);
    elements.Add(e => e.Handle).Label("Handle").Span(6);
    elements.Add(e => e.Description).Label("Description").Span(12);
    elements.Add(e => e.IsActive).Label("Active").AsCheckbox().Span(6);
  }
}
