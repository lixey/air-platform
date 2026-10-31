namespace Platform.App.Tests.Internals;

internal sealed class FixedUser(long userId, params string[] rights) : IUserContext
{
  private readonly HashSet<string> _rights = new(rights, StringComparer.OrdinalIgnoreCase);
  public long? UserId => userId;
  public long? SessionId => 1;
  public string? Login => "tester";
  public int TokenVersion => 1;
  public bool IsAuthenticated => true;
  public string Language => "en-US";
  public IReadOnlySet<string> Permissions => _rights;
  public bool Can(string right) => _rights.Contains("*") || _rights.Contains(right);
  public string? Claim(string type) => null;
}
