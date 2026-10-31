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
      foreach (var r in c.Records)
      {
        if (await IsLastActiveAdminRoleAsync(r.Id, r.TenantId, cancellationToken))
        {
          c.Refuse(L["The last administrator role cannot be deleted."], r.Id);
          return;
        }
      }

      var roles = c.Records.Select(r => r.Id).ToList();
      db.RemoveRange(await db.AppAuthAssignment.Where(a => roles.Contains(a.AuthRoleId)).ToListAsync(cancellationToken));
      db.RemoveRange(await db.AppAuthGrant.Where(g => roles.Contains(g.AuthRoleId)).ToListAsync(cancellationToken));
    });

  private async Task<bool> IsLastActiveAdminRoleAsync(long roleId, long? tenantId, CancellationToken cancellationToken)
  {
    if (!await db.AppAuthGrant.AnyAsync(g => g.AuthRoleId == roleId && g.Permission == "*", cancellationToken))
    {
      return false;
    }

    return !await db.AppAuthRole.AnyAsync(
      r => r.Id != roleId && r.TenantId == tenantId && r.IsActive &&
           db.AppAuthGrant.Any(g => g.AuthRoleId == r.Id && g.Permission == "*"),
      cancellationToken);
  }
}
