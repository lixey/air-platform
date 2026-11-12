namespace DemoApp.User;

public class AppUserFormService(AppDbContext db, AppService app)
  : AppFormService<AppUser>(db, app)
{
  public override Feature Feature => App.User;

  protected override async Task SetElementsAsync(Elements elements, CancellationToken cancellationToken)
  {
    elements.Add(e => e.Login).Unique("This login is already in use.");
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.Email).AsEmail().Unique("This email is already in use.").Span(6);
    elements.Add(e => e.LanguageId).AsReference<AppLanguageGridService>();
    elements.Add(e => e.TimeZone).AsTimeZone().Span(6);
    elements.Add(e => e.UserGroupId).AsReference<AppUserGroupGridService>();

    elements.AddTab("Security");
    elements.Add(e => e.Password).InputOnly().AsPassword().Span(6);
    elements.Add(e => e.IsActive).AsCheckbox();
    elements.Add(e => e.MustChangePassword).AsCheckbox();

    elements.AddTab("Roles");
    var offered = await GetOfferedRolesAsync(cancellationToken);
    elements.Add(e => e.RoleIds).InputOnly().AsCheckboxSet(offered);

    elements.AddChild<AppSessionGridService>().Key("sessions").Label("Sessions");
  }

  protected override void SetRules(Rules rules)
  {
    rules.For(e => e.Password).Custom((password, c) =>
    {
      var mutation = SaveContextOf(c);
      if (!mutation.IsNew && string.IsNullOrEmpty(password))
      {
        return;
      }

      if (string.IsNullOrEmpty(password) || password.Length < 10)
      {
        c.AddFailure(L["The password must be at least {0} characters.", 10]);
      }
      else if (password.Length > 128)
      {
        c.AddFailure(L["The password must be at most {0} characters.", 128]);
      }
      else if (!string.IsNullOrEmpty(mutation.Entity.Login) && string.Equals(password, mutation.Entity.Login, StringComparison.OrdinalIgnoreCase))
      {
        c.AddFailure(L["The password must not equal the login name."]);
      }
    });
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad(async (c, cancellationToken) =>
    {
      if (c.IsNew)
      {
        c.Entity.MustChangePassword = true;
        c.Entity.RoleIds = [];
        return;
      }

      var offeredIds = await GetOfferedRoleIdsAsync(cancellationToken);
      c.Entity.RoleIds = await db.Set<AppAuthAssignment>()
        .Where(r => r.UserId == c.Entity.Id && r.IsActive && offeredIds.Contains(r.AuthRoleId))
        .Select(r => r.AuthRoleId)
        .ToArrayAsync(cancellationToken);
    });

    hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (!c.IsNew && (app == null || !await PlatformAppService.CoversUserGrantsAsync(db, app, User, c.Entity.Id, cancellationToken)))
      {
        c.Refuse(L["You cannot modify a user with permissions you do not hold."]);
        return;
      }

      var offeredIds = await GetOfferedRoleIdsAsync(cancellationToken);
      if (c.Entity.Id == 1 || c.Entity.IsRoot)
      {
        if (User?.UserId is long actorId && actorId != c.Entity.Id)
        {
          var actorIsRoot = actorId == 1 || await db.Set<AppUser>()
            .Where(u => u.Id == actorId)
            .Select(u => u.IsRoot)
            .FirstOrDefaultAsync(cancellationToken);
          if (!actorIsRoot)
          {
            c.Refuse(L["The root user cannot be modified by another user."]);
            return;
          }
        }

        if (!c.Entity.IsActive)
        {
          c.Refuse(L["The root user cannot be deactivated."]);
          return;
        }

        var selectedRoles = (c.Entity.RoleIds ?? []).Where(offeredIds.Contains).ToHashSet();
        var hasAdminRole = await db.Set<AppAuthGrant>()
          .AnyAsync(g => g.Permission == "*" && (
            selectedRoles.Contains(g.AuthRoleId) ||
            db.Set<AppAuthAssignment>().Any(a => a.UserId == c.Entity.Id && a.IsActive && !offeredIds.Contains(a.AuthRoleId) && a.AuthRoleId == g.AuthRoleId)),
            cancellationToken);
        if (!hasAdminRole)
        {
          c.Refuse(L["The root user cannot be left without administrator rights."]);
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
        await PlatformAppService.RevokeUserSessionsAsync(db, [c.Entity.Id], "app-user-security-changed", cancellationToken, app);
      }
    });

    hooks.AfterSave(async (c, cancellationToken) =>
    {
      var offeredIds = await GetOfferedRoleIdsAsync(cancellationToken);
      var assignments = await db.Set<AppAuthAssignment>()
        .Where(r => r.UserId == c.Entity.Id && offeredIds.Contains(r.AuthRoleId))
        .ToListAsync(cancellationToken);
      var roles = (c.Entity.RoleIds ?? []).Where(offeredIds.Contains).ToHashSet();
      foreach (var assignment in assignments)
      {
        assignment.IsActive = roles.Contains(assignment.AuthRoleId);
      }

      foreach (var role in roles.Except(assignments.Select(r => r.AuthRoleId)))
      {
        db.Add(new AppAuthAssignment { UserId = c.Entity.Id, AuthRoleId = role, IsActive = true });
      }
    });
  }

  private async Task<List<OptionDto>> GetOfferedRolesAsync(CancellationToken cancellationToken)
  {
    var roles = await db.Set<AppAuthRole>().Where(r => r.IsActive).OrderBy(r => r.SortOrder)
      .Select(r => new
      {
        r.Id,
        r.Name,
        Grants = db.Set<AppAuthGrant>().Where(g => g.AuthRoleId == r.Id).Select(g => g.Permission).ToList()
      })
      .ToListAsync(cancellationToken);
    return roles
      .Where(r => app != null && app.Covers(r.Grants))
      .Select(r => new OptionDto { Id = r.Id, Name = L[r.Name] })
      .ToList();
  }

  private async Task<HashSet<long>> GetOfferedRoleIdsAsync(CancellationToken cancellationToken) =>
    [.. (await GetOfferedRolesAsync(cancellationToken)).Select(r => r.Id)];
}

