using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using Air.Data;
using DemoApp.Data;
using DemoApp.Data.Models;
using DemoApp.Tenant;
using DemoApp.UserGroup;
using Microsoft.EntityFrameworkCore;

namespace DemoApp.User;

public sealed class AppUserGridService(DemoDbContext db) : GridService<AppUser, AppUser>(db)
{
  public override Feature Feature => App.User;
  protected override string FormServicePath => "app/userform";

  protected override IQueryable<AppUser> Query(PlatformDbContext context) =>
    context.Set<AppUser>().Include(u => u.Tenant).Include(u => u.UserGroup);

  protected override void SetColumns(Columns<AppUser> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<AppUserFormService>();
    columns.Add(r => r.Login).Label("Login");
    columns.Add(r => r.Name).Label("Full Name");
    columns.Add(r => r.Email).Label("Email");
    columns.Add(r => r.Tenant).Label("Tenant").AsReference<AppTenantGridService>();
    columns.Add(r => r.UserGroup).Label("User Group").AsReference<AppUserGroupGridService>();
    columns.Add(r => r.TimeZone).Label("Time Zone");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class AppUserFormService(DemoDbContext db) : FormService<AppUser>(db)
{
  public override Feature Feature => App.User;
  protected override string Title => "User";

  protected override void SetElements(Elements<AppUser> elements)
  {
    elements.Add(e => e.Login).Label("Login").Span(6);
    elements.Add(e => e.Name).Label("Full Name").Span(6);
    elements.Add(e => e.Email).Label("Email Address").AsEmail().Span(6);
    elements.Add(e => e.TimeZone).Label("Time Zone").AsTimeZone().Span(6);
    elements.Add(e => e.UserGroupId).Label("User Group").AsReference<AppUserGroupGridService>().Span(6);

    elements.AddTab("Security");
    elements.Add(e => e.IsActive).Label("Active").AsCheckbox().Span(6);
    elements.Add(e => e.MustChangePassword).Label("Must Change Password").AsCheckbox().Span(6);
    elements.Add(e => e.PasswordHash).Label("Password").AsPassword().Span(6);
  }
}
