namespace DemoApp.Data.Models;

[Index(nameof(UserId), nameof(AuthRoleId), IsUnique = true)]
public sealed class AppAuthAssignment : BaseAppAuthAssignment
{
  public AppUser User { get; set; } = null!;
  public AppAuthRole AuthRole { get; set; } = null!;
}
