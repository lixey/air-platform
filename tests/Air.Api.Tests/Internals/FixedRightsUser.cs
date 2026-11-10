namespace Platform.Api.Tests.Internals;

internal sealed class FixedRightsUser(bool isAuthenticated, params string[] rights) : IUserContext
{
  private readonly HashSet<string> _rights = new(rights, StringComparer.OrdinalIgnoreCase);

  public long? UserId => isAuthenticated ? 10 : null;
  public long? SessionId => isAuthenticated ? 1 : null;
  public string? Login => isAuthenticated ? "tester" : null;
  public int TokenVersion => 1;
  public bool IsAuthenticated => isAuthenticated;
  public string Language => "en";
  public string TimeZone => "UTC";
  public IReadOnlySet<string> Permissions => _rights;
  public bool Can(string permission) => isAuthenticated && (_rights.Contains("*") || _rights.Contains(permission));
  public string? Claim(string type) => null;
}
