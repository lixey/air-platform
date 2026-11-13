namespace DemoApp.Role;

public sealed class AppRoleFormService(AppDbContext db, AppService app)
  : AppFormService<AppAuthRole>(db, app)
{
  public override Feature Feature => App.Role;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.TenantId).AsReference<AppTenantGridService>();
    Elements.Add(e => e.SortOrder);
    Elements.Add(e => e.IsActive).AsCheckbox();
    Elements.Add(e => e.Description).AsTextarea();

    Elements.AddTab("Rights");
    Elements.Add(e => e.Permissions).InputOnly().AsRightsTree();

    Elements.AddChild<AppRoleUserGridService>().Key("users").Label("Users");
  }

  protected override void SetRules() =>
    AppRoleSecurityRules.AddNameRules(this);

  protected override void SetHooks() =>
    AppRoleSecurityRules.AddFormHooks<AppAuthRole, AppAuthGrant>(this);
}
