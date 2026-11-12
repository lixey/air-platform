namespace DemoApp.Role;

public sealed class AppRoleGridService(AppDbContext db, AppService app)
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
      foreach (var r in c.Records)
      {
        if (app == null || !await PlatformAppService.CoversRoleGrantsAsync(db, app, User, r.Id, cancellationToken))
        {
          c.Refuse(L["You cannot modify a role with permissions you do not hold."], r.Id);
          return;
        }
      }

      var adminRoleIds = (await db.AppAuthGrant
        .Where(g => roles.Contains(g.AuthRoleId) && g.Permission == "*")
        .Select(g => g.AuthRoleId)
        .Distinct()
        .ToListAsync(cancellationToken))
        .ToHashSet();

      foreach (var r in c.Records.Where(r => adminRoleIds.Contains(r.Id)))
      {
        var tenantId = r.TenantId;
        var hasRemainingActiveAdminRole = await db.AppAuthRole.AnyAsync(
          other => !roles.Contains(other.Id) && other.TenantId == tenantId && other.IsActive &&
                   db.AppAuthGrant.Any(g => g.AuthRoleId == other.Id && g.Permission == "*"),
          cancellationToken);
        if (!hasRemainingActiveAdminRole)
        {
          c.Refuse(L["The last administrator role cannot be deleted."], r.Id);
          return;
        }
      }

      db.RemoveRange(await db.AppAuthAssignment.Where(a => roles.Contains(a.AuthRoleId)).ToListAsync(cancellationToken));
      db.RemoveRange(await db.AppAuthGrant.Where(g => roles.Contains(g.AuthRoleId)).ToListAsync(cancellationToken));
    });
}
