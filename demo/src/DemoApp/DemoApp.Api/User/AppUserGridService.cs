using Air.App.Components;
using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Color;
using DemoApp.Data;
using DemoApp.Data.Models;
using DemoApp.Language;
using DemoApp.Tenant;
using DemoApp.UserGroup;

namespace DemoApp.User;

public class AppUserGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppUser>(db, app)
{
  public override Feature Feature => App.User;

  protected override void SetColumns(Columns<AppUser> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppUserFormService>();
    columns.Add(r => r.Login);
    columns.Add(r => r.Name);
    columns.Add(r => r.Email);
    columns.Add(r => r.Tenant).AsReference<AppTenantGridService>();
    columns.Add(r => r.UserGroup).AsReference<AppUserGroupGridService>();
    columns.Add(r => r.Language).AsReference<AppLanguageGridService>();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Login);

  protected override void SetNav(Nav nav)
  {
    nav.AddAction(Deactivate, b => b.Label(L["Deactivate"]).Icon(UiIcon.Ban), L["Deactivate the selected users and end their sessions?"]);
  }

  public Task<GridActionDto> Deactivate(GridRequest request, CancellationToken cancellationToken) =>
    ExecuteBatchAsync(request, (context, token) =>
    {
      foreach (var user in context.Records)
      {
        user.IsActive = false;
      }
      return Task.CompletedTask;
    }, cancellationToken);
}
