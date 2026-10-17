namespace DemoApp.Data.Models;

[Index(nameof(AuthRoleId), nameof(Permission), IsUnique = true)]
public sealed class AppAuthGrant : BaseAppAuthGrant
{
  public AppAuthRole AuthRole { get; set; } = null!;
}
