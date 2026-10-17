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
  public override Feature Feature => App.User;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override async Task SetElementsAsync(Elements elements, CancellationToken cancellationToken)
  {
    elements.Add(e => e.Login).Unique("This login is already in use.").Span(2);
    elements.Add(e => e.Name).Span(2);
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
      .Select(r => new CheckboxOptionDto { Id = r.Id, Label = r.Name })
      .ToListAsync(cancellationToken);
    elements.Add(e => e.RoleIds).InputOnly().AsCheckboxSet(roles);

    elements.AddChild<AppSessionGridService>().Key("sessions").Label("Sessions");
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad(async (c, cancellationToken) =>
    {
      c.Input.RoleIds = c.IsNew
        ? []
        : await db.Set<AppAuthAssignment>()
            .Where(r => r.UserId == c.Entity.Id && r.IsActive)
            .Select(r => r.AuthRoleId)
            .ToArrayAsync(cancellationToken);
    });

    hooks.AfterSave(async (c, cancellationToken) =>
    {
      var assignments = await db.Set<AppAuthAssignment>().Where(r => r.UserId == c.Entity.Id).ToListAsync(cancellationToken);
      var roles = (c.Input.RoleIds ?? []).ToHashSet();
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
