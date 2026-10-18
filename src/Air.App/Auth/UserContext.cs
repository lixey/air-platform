using System.Globalization;
using System.Security.Claims;
using Air.Data.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Air.App.Auth;

/// <summary>
/// IUserContext read from the claims of the current request.
/// </summary>
public class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
  private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;
  private HashSet<string>? _permissions;

  public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

  public long? UserId => Long(AuthClaims.Sub) ?? Long(ClaimTypes.NameIdentifier);

  public long? SessionId => Long(AuthClaims.SessionId);

  public string? Login => User?.FindFirst(AuthClaims.Login)?.Value ?? User?.FindFirst(ClaimTypes.Name)?.Value;

  public int TokenVersion => int.TryParse(User?.FindFirst(AuthClaims.Version)?.Value, out var version) ? version : 1;

  public bool MustChangePassword => User?.HasClaim(AuthClaims.MustChangePassword, "true") == true;

  public string Language => CultureInfo.CurrentUICulture.Name;

  public IReadOnlySet<string> Permissions => _permissions ??= Values(AuthClaims.Permission);

  public string? Claim(string type) => User?.FindFirst(type)?.Value;

  public bool Can(string permission) =>
    IsAuthenticated && !string.IsNullOrWhiteSpace(permission) && HeldRights.Covers(Permissions, permission);

  private long? Long(string claim) => long.TryParse(User?.FindFirst(claim)?.Value, out var value) ? value : null;

  private HashSet<string> Values(string claim) =>
    (User?.FindAll(claim) ?? [])
    .Select(c => c.Value)
    .Where(v => !string.IsNullOrWhiteSpace(v))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
