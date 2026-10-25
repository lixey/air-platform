namespace Platform.Api.Auth;

/// <summary>
/// Claim and cookie names used by the platform security and session management.
/// </summary>
public static class AuthClaims
{
  public const string Sub = "sub";
  public const string Login = "login";
  public const string SessionId = "sid";
  public const string Version = "ver";
  public const string Permission = "permission";
  public const string MustChangePassword = "must_change_password";
  public const string Tenant = "tenant";
  public const string ViaCookieItem = "Platform.Auth.ViaCookie";
  public const string SnapshotItem = "Platform.Auth.Snapshot";
  public const string AccessCookie = "platform_access";
  public const string RefreshCookie = "platform_refresh";
}
