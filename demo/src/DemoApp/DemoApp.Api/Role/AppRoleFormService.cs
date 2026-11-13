namespace DemoApp.Role;

public sealed class AppRoleFormService(AppDbContext db, AppService app)
  : AppFormService<AppAuthRole>(db, app)
{
  public override Feature Feature => App.Role;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.TenantId).AsReference<AppTenantGridService>();
    elements.Add(e => e.SortOrder);
    elements.Add(e => e.IsActive).AsCheckbox();
    elements.Add(e => e.Description).AsTextarea();

    elements.AddTab("Rights");
    elements.Add(e => e.Permissions).InputOnly().AsRightsTree();

    elements.AddChild<AppRoleUserGridService>().Key("users").Label("Users");
  }

  protected override void SetRules(Rules rules) =>
    AppRoleSecurityRules.AddNameRules(this, rules);

  protected override void SetHooks(Hooks hooks) =>
    AppRoleSecurityRules.AddFormHooks<AppAuthRole, AppAuthGrant>(this, hooks);
}
