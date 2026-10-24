namespace Platform.Api.Auth.Contracts;

[UiContract]
public sealed class LoginScreenDto
{
  public string Title { get; set; } = string.Empty;
  public string Login { get; set; } = string.Empty;
  public string Password { get; set; } = string.Empty;
  public string Submit { get; set; } = string.Empty;
  public string InvalidCredentials { get; set; } = string.Empty;
  public string TooManyAttempts { get; set; } = string.Empty;
  public string Unavailable { get; set; } = string.Empty;
  public string ChangePasswordTitle { get; set; } = string.Empty;
  public string CurrentPassword { get; set; } = string.Empty;
  public string NewPassword { get; set; } = string.Empty;
  public string RepeatPassword { get; set; } = string.Empty;
  public string ChangePasswordSubmit { get; set; } = string.Empty;
  public string PasswordsDiffer { get; set; } = string.Empty;
  public string SignOut { get; set; } = string.Empty;
  public string Culture { get; set; } = string.Empty;
}
