using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp.UserGroup;

public sealed class AppUserGroupGridService(DemoDbContext db) : GridService<AppUserGroup, AppUserGroup>(db)
{
  public override Feature Feature => App.UserGroup;
  protected override string FormServicePath => "app/usergroupform";

  protected override void SetColumns(Columns<AppUserGroup> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<AppUserGroupFormService>();
    columns.Add(r => r.Name).Label("Group Name");
    columns.Add(r => r.Handle).Label("Handle");
    columns.Add(r => r.Color).Label("Color");
    columns.Add(r => r.Description).Label("Description");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class AppUserGroupFormService(DemoDbContext db) : FormService<AppUserGroup>(db)
{
  public override Feature Feature => App.UserGroup;
  protected override string Title => "User Group";

  protected override void SetElements(Elements<AppUserGroup> elements)
  {
    elements.Add(e => e.Name).Label("Group Name").Span(6);
    elements.Add(e => e.Handle).Label("Handle").Span(6);
    elements.Add(e => e.Color).Label("Color").Span(6);
    elements.Add(e => e.Description).Label("Description").Span(12);
    elements.Add(e => e.IsActive).Label("Active").AsCheckbox().Span(6);
  }
}
