namespace Platform.Api.Auth.Contracts;

[UiContract]
public class SessionDto
{
  public UserProfileDto User { get; set; } = new();
  public int ExpiresIn { get; set; }
}
