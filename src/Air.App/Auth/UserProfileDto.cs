namespace Air.App.Auth;

[UiContract]
public class UserProfileDto
{
  public long Id { get; set; }
  public string Login { get; set; } = string.Empty;
  public string? Name { get; set; }
  public string? Email { get; set; }
  public string Language { get; set; } = string.Empty;
  public string TimeZone { get; set; } = "UTC";
  public bool MustChangePassword { get; set; }
  public List<string> Permissions { get; set; } = [];
  public Dictionary<string, string> Claims { get; set; } = [];
}
