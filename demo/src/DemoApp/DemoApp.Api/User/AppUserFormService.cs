using Air.App.Components;
using Air.App.Form;
using Air.App.Modules;
using Air.App.Overlay;
using DemoApp.Data;
using DemoApp.Data.Models;
using DemoApp.Language;
using DemoApp.Session;
using DemoApp.UserGroup;
using Microsoft.EntityFrameworkCore;

namespace DemoApp.User;

public class AppUserFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppUser, AppUserEdit>(db, app)
{
  private HashSet<long> _offered = [];

  public override Feature Feature => App.User;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override async Task SetElementsAsync(Elements elements, CancellationToken cancellationToken)
  {
    elements.Add(e => e.Login).Unique("This login is already in use.").Span(2);
    elements.Add(e => e.Name).Focus().Span(2);
    elements.Add(e => e.Email).AsEmail().Unique("This email is already in use.");
    elements.Add(e => e.LanguageId).AsReference<AppLanguageGridService>().Span(2);
    elements.Add(e => e.TimeZone).AsTimeZone().Span(2);
    elements.Add(e => e.UserGroupId).AsReference<AppUserGroupGridService>().Span(2);

    elements.AddTab("Security");
    elements.Add(e => e.IsActive).AsCheckbox();
    elements.Add(e => e.MustChangePassword).AsCheckbox();
    elements.Add(e => e.Password).InputOnly().AsPassword();

    elements.AddTab("Roles");
    var roles = await db.Set<AppAuthRole>().Where(r => r.IsActive).OrderBy(r => r.SortOrder)
      .Select(r => new
      {
        r.Id,
        r.Name,
        Grants = db.Set<AppAuthGrant>().Where(g => g.AuthRoleId == r.Id).Select(g => g.Permission).ToList()
      })
      .ToListAsync(cancellationToken);
    var offered = roles
      .Where(r => app == null || app.Covers(r.Grants))
      .Select(r => new CheckboxOptionDto { Id = r.Id, Label = L[r.Name] })
      .ToList();
    _offered = [.. offered.Select(r => r.Id)];
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
      else if (!string.IsNullOrEmpty(mutation.Input.Login) && string.Equals(password, mutation.Input.Login, StringComparison.OrdinalIgnoreCase))
      {
        c.AddFailure(L["The password must not equal the login name."]);
      }
    });
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.BeforeDelete(async (c, cancellationToken) =>
    {
      if (c.Entity.Id == 1)
      {
        c.Refuse(L["The root user cannot be deleted."]);
        return;
      }

      var now = DateTimeOffset.UtcNow;
      var sessions = await db.AppAuthSession
        .Where(s => s.UserId == c.Entity.Id && s.RevokedAt == null)
        .ToListAsync(cancellationToken);
      foreach (var session in sessions)
      {
        session.RevokedAt = now;
        session.RevokedReason = "demo-user-deleted";
      }
    });

    hooks.AfterLoad(async (c, cancellationToken) =>
    {
      c.Input.RoleIds = c.IsNew
        ? []
        : await db.Set<AppAuthAssignment>()
            .Where(r => r.UserId == c.Entity.Id && r.IsActive && _offered.Contains(r.AuthRoleId))
            .Select(r => r.AuthRoleId)
            .ToArrayAsync(cancellationToken);
    });

    hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (c.Entity.Id == 1 && !c.Input.IsActive)
      {
        c.Refuse(L["The root user cannot be deactivated."]);
        return;
      }

      if (!string.IsNullOrEmpty(c.Input.Password))
      {
        c.Entity.PasswordHash = (app?.Hasher ?? new Air.App.Auth.PasswordHasher()).HashPassword(c.Input.Password);
      }

      if (!c.IsNew && c.Changed(e => e.Login, e => e.IsActive, e => e.MustChangePassword, e => e.Password, e => e.RoleIds))
      {
        var now = DateTimeOffset.UtcNow;
        var sessions = await db.AppAuthSession
          .Where(s => s.UserId == c.Entity.Id && s.RevokedAt == null)
          .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
          session.RevokedAt = now;
          session.RevokedReason = "app-user-security-changed";
        }
      }
    });

    hooks.AfterSave(async (c, cancellationToken) =>
    {
      var assignments = await db.Set<AppAuthAssignment>()
        .Where(r => r.UserId == c.Entity.Id && _offered.Contains(r.AuthRoleId))
        .ToListAsync(cancellationToken);
      var roles = (c.Input.RoleIds ?? []).Where(_offered.Contains).ToHashSet();
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
}

