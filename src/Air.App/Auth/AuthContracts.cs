namespace Air.App.Auth;

[UiContract]
public class SessionDto
{
  public UserProfileDto User { get; set; } = new();
  public int ExpiresIn { get; set; }
}

[UiContract]
public class LoginRequest
{
  public string Login { get; set; } = string.Empty;
  public string Password { get; set; } = string.Empty;
}

[UiContract]
public class RefreshRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? RefreshToken { get; set; }
}

[UiContract]
public class ChangePasswordRequest
{
  public string CurrentPassword { get; set; } = string.Empty;
  public string NewPassword { get; set; } = string.Empty;
}
