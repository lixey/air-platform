using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Data;
using DemoApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoApp.Role;

public sealed class AppRoleUserGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAuthAssignment>(db, app)
{
  public override Feature Feature => App.Role;
  protected override bool IsReadOnly => true;

  protected override void SetParent(Parent parent) => parent.Bind<AppRoleFormService>(e => e.AuthRoleId);

  protected override void SetColumns(Columns<AppAuthAssignment> columns)
  {
    columns.Add(r => r.User.Login);
    columns.Add(r => r.User.Name);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) =>
    query.Where(assignment => db.AppUser.Any(user => user.Id == assignment.UserId));
}
