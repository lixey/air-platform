namespace Platform.App.Auth;

/// <summary>
/// Shared RBAC, root-user, password, and session-revocation rules and hooks for application <see cref="BaseAppUser"/> services.
/// </summary>
public static class AppUserSecurityRules
{
  public static async Task<List<OptionDto>> GetOfferedRolesAsync<TRole, TGrant>(
    PlatformDbContext db,
    PlatformAppService? app,
    ILocalizer? localizer = null,
    CancellationToken ct = default)
    where TRole : BaseAppAuthRole
    where TGrant : BaseAppAuthGrant
  {
    ArgumentNullException.ThrowIfNull(db);

    var roles = await db.Set<TRole>()
      .Where(r => r.IsActive)
      .OrderBy(r => r.SortOrder)
      .Select(r => new
      {
        r.Id,
        r.Name,
        Grants = db.Set<TGrant>().Where(g => g.AuthRoleId == r.Id).Select(g => g.Permission).ToList()
      })
      .ToListAsync(ct);

    return
    [
      .. roles
        .Where(r => app != null && app.Covers(r.Grants))
        .Select(r => new OptionDto { Id = r.Id, Name = localizer != null ? localizer[r.Name] : r.Name })
    ];
  }

  public static async Task<HashSet<long>> GetOfferedRoleIdsAsync<TRole, TGrant>(
    PlatformDbContext db,
    PlatformAppService? app,
    CancellationToken ct = default)
    where TRole : BaseAppAuthRole
    where TGrant : BaseAppAuthGrant =>
    [.. (await GetOfferedRolesAsync<TRole, TGrant>(db, app, localizer: null, ct)).Select(r => r.Id)];

  public static Task<List<OptionDto>> GetOfferedRolesAsync<TUser, TRole, TGrant>(
    FormService<TUser> service,
    CancellationToken ct = default)
    where TUser : BaseAppUser, new()
    where TRole : BaseAppAuthRole
    where TGrant : BaseAppAuthGrant =>
    GetOfferedRolesAsync<TRole, TGrant>(service.DbContext, service.PlatformApp, service.L, ct);

  public static void AddFormHooks<TUser, TRole, TGrant, TAssignment>(
    FormService<TUser> service)
    where TUser : BaseAppUser, new()
    where TRole : BaseAppAuthRole
    where TGrant : BaseAppAuthGrant
    where TAssignment : BaseAppAuthAssignment, new() =>
    AddFormHooks<TUser, TRole, TGrant, TAssignment>(service.Hooks, service.DbContext, service.PlatformApp, () => service.User);

  public static void AddDeleteHooks<TUser>(
    GridService<TUser, TUser> service)
    where TUser : BaseAppUser, new() =>
    AddDeleteHooks(service.Hooks, service.DbContext, service.PlatformApp, () => service.User);

  public static void AddDeactivateAction<TUser>(
    GridService<TUser, TUser> service)
    where TUser : BaseAppUser, new() =>
    AddDeactivateAction(service.Actions, service.DbContext, service.PlatformApp, () => service.User);

  public static void AddPasswordRules<TUser>(FormService<TUser> service)
    where TUser : BaseAppUser, new() =>
    AddPasswordRules(service.Rules);

  public static void AddPasswordRules<TUser>(FormRules<TUser> rules)
    where TUser : BaseAppUser, new()
  {
    var l = Localizer.Instance;
    rules.For(e => e.Password).Custom((password, c) =>
    {
      if (!c.IsNew && string.IsNullOrEmpty(password))
      {
        return;
      }

      if (string.IsNullOrEmpty(password) || password.Length < 10)
      {
        c.AddFailure(l["PasswordTooShort", 10]);
      }
      else if (password.Length > 128)
      {
        c.AddFailure(l["PasswordTooLong", 128]);
      }
      else if (!string.IsNullOrEmpty(c.Entity.Login) &&
               string.Equals(password, c.Entity.Login, StringComparison.OrdinalIgnoreCase))
      {
        c.AddFailure(l["PasswordIsLogin"]);
      }
    });
  }

  public static void AddFormHooks<TUser, TRole, TGrant, TAssignment>(
    FormHooks<TUser> hooks,
    PlatformDbContext db,
    PlatformAppService? app,
    Func<IUserContext?> userProvider)
    where TUser : BaseAppUser, new()
    where TRole : BaseAppAuthRole
    where TGrant : BaseAppAuthGrant
    where TAssignment : BaseAppAuthAssignment, new()
  {
    var l = Localizer.Instance;

    hooks.AfterLoad(async (c, ct) =>
    {
      if (c.IsNew)
      {
        c.Entity.MustChangePassword = true;
        c.Entity.RoleIds = [];
        return;
      }

      var offeredIds = await GetOfferedRoleIdsAsync<TRole, TGrant>(db, app, ct);
      c.Entity.RoleIds = await db.Set<TAssignment>()
        .Where(r => r.UserId == c.Entity.Id && r.IsActive && offeredIds.Contains(r.AuthRoleId))
        .Select(r => r.AuthRoleId)
        .ToArrayAsync(ct);
    });

    hooks.BeforeSave(async (c, ct) =>
    {
      var user = userProvider();
      if (!c.IsNew && (app == null || !await PlatformAppService.CoversUserGrantsAsync(db, app, user, c.Entity.Id, ct)))
      {
        c.Refuse(l["UserOutOfPermissionScope"]);
        return;
      }

      var offeredIds = await GetOfferedRoleIdsAsync<TRole, TGrant>(db, app, ct);
      if (c.Entity.Id == 1 || c.Entity.IsRoot)
      {
        if (user?.UserId is { } actorId && actorId != c.Entity.Id)
        {
          var actorIsRoot = actorId == 1 || await db.Set<TUser>()
            .Where(u => u.Id == actorId)
            .Select(u => u.IsRoot)
            .FirstOrDefaultAsync(ct);
          if (!actorIsRoot)
          {
            c.Refuse(l["RootUserCannotBeModifiedByOther"]);
            return;
          }
        }

        if (!c.Entity.IsActive)
        {
          c.Refuse(l["RootUserCannotBeDeactivated"]);
          return;
        }

        var selectedRoles = (c.Entity.RoleIds ?? []).Where(offeredIds.Contains).ToHashSet();
        var hasAdminRole = await db.Set<TGrant>()
          .AnyAsync(
            g => g.Permission == "*" && (
              selectedRoles.Contains(g.AuthRoleId) ||
              db.Set<TAssignment>().Any(a =>
                a.UserId == c.Entity.Id &&
                a.IsActive &&
                !offeredIds.Contains(a.AuthRoleId) &&
                a.AuthRoleId == g.AuthRoleId)),
            ct);
        if (!hasAdminRole)
        {
          c.Refuse(l["RootUserCannotLoseAdmin"]);
          return;
        }
      }

      if (!string.IsNullOrEmpty(c.Entity.Password))
      {
        c.Entity.PasswordHash = (app?.Hasher ?? new PasswordHasher()).HashPassword(c.Entity.Password);
      }

      if (!c.IsNew && c.Changed(e => e.Login, e => e.IsActive, e => e.MustChangePassword, e => e.Password, e => e.RoleIds))
      {
        c.Entity.TokenVersion++;
        await PlatformAppService.RevokeUserSessionsAsync(db, [c.Entity.Id], "app-user-security-changed", ct, app);
      }
    });

    hooks.AfterSave(async (c, ct) =>
    {
      var offeredIds = await GetOfferedRoleIdsAsync<TRole, TGrant>(db, app, ct);
      var assignments = await db.Set<TAssignment>()
        .Where(r => r.UserId == c.Entity.Id && offeredIds.Contains(r.AuthRoleId))
        .ToListAsync(ct);
      var roles = (c.Entity.RoleIds ?? []).Where(offeredIds.Contains).ToHashSet();
      foreach (var assignment in assignments)
      {
        assignment.IsActive = roles.Contains(assignment.AuthRoleId);
      }

      foreach (var role in roles.Except(assignments.Select(r => r.AuthRoleId)))
      {
        db.Add(new TAssignment { UserId = c.Entity.Id, AuthRoleId = role, IsActive = true });
      }
    });
  }

  public static void AddDeleteHooks<TUser>(
    GridHooks<TUser> hooks,
    PlatformDbContext db,
    PlatformAppService? app,
    Func<IUserContext?> userProvider)
    where TUser : BaseAppUser, new()
  {
    var l = Localizer.Instance;
    hooks.BeforeDelete(async (c, ct) =>
    {
      if (c.Records.Any(u => u.Id == 1 || u.IsRoot))
      {
        c.Refuse(l["RootUserCannotBeDeleted"]);
        return;
      }

      var user = userProvider();
      foreach (var target in c.Records)
      {
        if (app == null || !await PlatformAppService.CoversUserGrantsAsync(db, app, user, target.Id, ct))
        {
          c.Refuse(l["UserOutOfPermissionScope"], target.Id);
          return;
        }
      }

      await PlatformAppService.RevokeUserSessionsAsync(db, [.. c.Records.Select(u => u.Id)], "app-user-deleted", ct, app);
    });
  }

  public static void AddDeactivateAction<TUser>(
    GridActions<TUser> actions,
    PlatformDbContext db,
    PlatformAppService? app,
    Func<IUserContext?> userProvider)
    where TUser : BaseAppUser
  {
    var l = Localizer.Instance;
    actions.Add("Deactivate")
      .AllowEdit()
      .Label("Deactivate")
      .Icon(UiIcon.Ban)
      .Confirm("Deactivate the selected users and end their sessions?")
      .Do(async (ctx, ct) =>
      {
        if (ctx.Records.Any(u => u.Id == 1 || u.IsRoot))
        {
          ctx.Refuse(l["RootUserCannotBeDeactivated"]);
          return;
        }

        var user = userProvider();
        foreach (var target in ctx.Records)
        {
          if (app == null || !await PlatformAppService.CoversUserGrantsAsync(db, app, user, target.Id, ct))
          {
            ctx.Refuse(l["UserOutOfPermissionScope"], target.Id);
            return;
          }
        }

        foreach (var target in ctx.Records)
        {
          target.IsActive = false;
        }

        await PlatformAppService.RevokeUserSessionsAsync(db, [.. ctx.Records.Select(u => u.Id)], "app-user-deactivated", ct, app);
      });
  }
}
