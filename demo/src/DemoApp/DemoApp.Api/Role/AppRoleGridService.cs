using Air.App.Grid;
using Air.App.Modules;
using DemoApp.Data;
using DemoApp.Data.Models;
using DemoApp.Tenant;
using Microsoft.EntityFrameworkCore;

namespace DemoApp.Role;

public sealed class AppRoleGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAuthRole>(db, app)
{
  public override Feature Feature => App.Role;

  protected override void SetColumns(Columns<AppAuthRole> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppRoleFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Tenant).AsReference<AppTenantGridService>();
    columns.Add(r => r.Description);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);

  protected override void SetHooks(Hooks hooks) =>
    hooks.BeforeDelete(async (c, cancellationToken) =>
    {
      var roles = c.Records.Select(r => r.Id).ToList();
      db.RemoveRange(await db.AppAuthAssignment.Where(a => roles.Contains(a.AuthRoleId)).ToListAsync(cancellationToken));
      db.RemoveRange(await db.AppAuthGrant.Where(g => roles.Contains(g.AuthRoleId)).ToListAsync(cancellationToken));
    });
}
