namespace DemoApp.User;

public class AppUserGridService(AppDbContext db, AppService app)
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
        if (app == null || !await PlatformAppService.CoversUserGrantsAsync(db, app, User, u.Id, cancellationToken))
        {
          c.Refuse(L["You cannot modify a user with permissions you do not hold."], u.Id);
          return;
        }
      }

      await PlatformAppService.RevokeUserSessionsAsync(db, [.. c.Records.Select(u => u.Id)], "app-user-deleted", cancellationToken, app);
    });
  }

  protected override void SetActions(Actions actions) =>
    actions.Add("Deactivate")
      .AllowEdit()
      .Label("Deactivate")
      .Icon(UiIcon.Ban)
      .Confirm("Deactivate the selected users and end their sessions?")
      .Do(async (ctx, ct) =>
      {
        if (ctx.Records.Any(u => u.Id == 1 || u.IsRoot))
        {
          ctx.Refuse(L["The root user cannot be deactivated."]);
          return;
        }

        foreach (var u in ctx.Records)
        {
          if (app == null || !await PlatformAppService.CoversUserGrantsAsync(db, app, User, u.Id, ct))
          {
            ctx.Refuse(L["You cannot modify a user with permissions you do not hold."], u.Id);
            return;
          }
        }

        foreach (var user in ctx.Records)
        {
          user.IsActive = false;
        }

        await PlatformAppService.RevokeUserSessionsAsync(db, [.. ctx.Records.Select(u => u.Id)], "app-user-deactivated", ct, app);
      });
}

