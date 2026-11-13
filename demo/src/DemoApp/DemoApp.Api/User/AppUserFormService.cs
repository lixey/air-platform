namespace DemoApp.User;

public sealed class AppUserFormService(AppDbContext db, AppService app)
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
    var offered = await AppUserSecurityRules.GetOfferedRolesAsync<AppAuthRole, AppAuthGrant>(db, app, L, cancellationToken);
    elements.Add(e => e.RoleIds).InputOnly().AsCheckboxSet(offered);

    elements.AddChild<AppSessionGridService>().Key("sessions").Label("Sessions");
  }

  protected override void SetRules(Rules rules) =>
    AppUserSecurityRules.AddPasswordRules(rules);

  protected override void SetHooks(Hooks hooks) =>
    AppUserSecurityRules.AddFormHooks<AppUser, AppAuthRole, AppAuthGrant, AppAuthAssignment>(hooks, db, app, () => User);
}
