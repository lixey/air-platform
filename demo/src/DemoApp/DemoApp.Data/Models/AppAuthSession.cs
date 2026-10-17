namespace DemoApp.Data.Models;

[Index(nameof(UserId), nameof(RevokedAt)), Index(nameof(FamilyId)), Index(nameof(ExpiresAt))]
public sealed class AppAuthSession : BaseAppAuthSession
{
  public AppUser User { get; set; } = null!;
}
