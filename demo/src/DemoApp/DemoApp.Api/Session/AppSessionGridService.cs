using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Data;
using DemoApp.Data.Models;
using DemoApp.User;

namespace DemoApp.Session;

public sealed class AppSessionGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAuthSession>(db, app)
{
  public override Feature Feature => App.Session;

  protected override void SetParent(Parent parent) => parent.Bind<AppUserFormService>(e => e.UserId);

  protected override void SetColumns(Columns<AppAuthSession> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Client);
    columns.Add(r => r.Address);
    columns.Add(r => r.LastUsedAt);
    columns.Add(r => r.ExpiresAt);
    columns.Add(r => r.RevokedAt);
    columns.Add(r => r.RevokedReason);
  }
}
