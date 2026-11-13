namespace DemoApp.User;

public sealed class AppUserFormService(AppDbContext db, AppService app)
  : AppFormService<AppUser>(db, app)
{
  public override Feature Feature => App.User;

  protected override async Task SetElementsAsync(CancellationToken cancellationToken)
  {
    Elements.Add(e => e.Login).Unique("This login is already in use.");
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.Email).AsEmail().Unique("This email is already in use.").Span(6);
    Elements.Add(e => e.LanguageId).AsReference<AppLanguageGridService>();
    Elements.Add(e => e.TimeZone).AsTimeZone().Span(6);
    Elements.Add(e => e.UserGroupId).AsReference<AppUserGroupGridService>();

    Elements.AddTab("Security");
    Elements.Add(e => e.Password).InputOnly().AsPassword().Span(6);
    Elements.Add(e => e.IsActive).AsCheckbox();
    Elements.Add(e => e.MustChangePassword).AsCheckbox();

    Elements.AddTab("Roles");
    var offered = await AppUserSecurityRules.GetOfferedRolesAsync<AppUser, AppAuthRole, AppAuthGrant>(this, cancellationToken);
    Elements.Add(e => e.RoleIds).InputOnly().AsCheckboxSet(offered);

    Elements.AddChild<AppSessionGridService>().Key("sessions").Label("Sessions");
  }

  protected override void SetRules() =>
    AppUserSecurityRules.AddPasswordRules(this);

  protected override void SetHooks() =>
    AppUserSecurityRules.AddFormHooks<AppUser, AppAuthRole, AppAuthGrant, AppAuthAssignment>(this);
}
