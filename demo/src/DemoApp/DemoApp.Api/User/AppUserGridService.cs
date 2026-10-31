namespace DemoApp.User;

public class AppUserGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppUser>(db, app)
{
  public override Feature Feature => App.User;

  protected override string FiltersWidth => PanelSize.Px(960);

  protected override void SetColumns(Columns<AppUser> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppUserFormService>();
    columns.Add(r => r.Login);
    columns.Add(r => r.Name);
    columns.Add(r => r.Email);
    columns.Add(r => r.Tenant).AsReference<AppTenantGridService>();
    columns.Add(r => r.UserGroup).AsReference<AppUserGroupGridService>().Color(u => u.UserGroup!.Color.Handle);
    columns.Add(r => r.Language).AsReference<AppLanguageGridService>();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Login);

  protected override void SetHooks(Hooks hooks)
  {
    hooks.BeforeDelete(async (c, cancellationToken) =>
    {
      if (c.Records.Any(u => u.Id == 1 || u.IsRoot))
      {
        c.Refuse(L["The root user cannot be deleted."]);
        return;
      }

      foreach (var u in c.Records)
      {
        if (User?.UserId != u.Id && !await CoversUserGrantsAsync(u.Id, cancellationToken))
        {
          c.Refuse(L["You cannot modify a user with permissions you do not hold."], u.Id);
          return;
        }
      }

      var ids = c.Records.Select(u => u.Id).ToList();
      var now = DateTimeOffset.UtcNow;
      var sessions = await db.AppAuthSession
        .Where(s => ids.Contains(s.UserId) && s.RevokedAt == null)
        .ToListAsync(cancellationToken);
      foreach (var session in sessions)
      {
        session.RevokedAt = now;
        session.RevokedReason = "demo-user-deleted";
      }
    });
  }

  private async Task<bool> CoversUserGrantsAsync(long userId, CancellationToken cancellationToken)
  {
    var grants = await db.Set<AppAuthGrant>()
      .Where(g => db.Set<AppAuthAssignment>().Any(a => a.UserId == userId && a.IsActive && a.AuthRoleId == g.AuthRoleId))
      .Select(g => g.Permission)
      .Distinct()
      .ToListAsync(cancellationToken);
    if (app != null)
    {
      return app.Covers(grants);
    }

    var held = db.TenantContext.Rights ?? User?.Permissions;
    return held != null && grants.All(p => HeldRights.Covers(held, p));
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(Deactivate, b => b.Label(L["Deactivate"]).Icon(UiIcon.Ban), L["Deactivate the selected users and end their sessions?"]);
  }

  public Task<GridActionDto> Deactivate(GridRequest request, CancellationToken cancellationToken) =>
    ExecuteBatchAsync(request, async (context, token) =>
    {
      if (context.Records.Any(u => u.Id == 1))
      {
        context.Refuse(L["The root user cannot be deactivated."]);
        return;
      }

      var ids = context.Records.Select(u => u.Id).ToList();
      var now = DateTimeOffset.UtcNow;
      foreach (var user in context.Records)
      {
        user.IsActive = false;
      }

      var sessions = await db.AppAuthSession
        .Where(s => ids.Contains(s.UserId) && s.RevokedAt == null)
        .ToListAsync(token);
      foreach (var session in sessions)
      {
        session.RevokedAt = now;
        session.RevokedReason = "demo-user-deactivated";
      }
    }, cancellationToken);
}

