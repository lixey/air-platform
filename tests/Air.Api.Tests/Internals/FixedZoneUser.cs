namespace Platform.Api.Tests.Internals;

internal sealed class FixedZoneUser(string timeZone) : IUserContext
{
  public long? UserId => 10;
  public long? SessionId => 1;
  public string? Login => "tester";
  public int TokenVersion => 1;
  public bool IsAuthenticated => true;
  public string Language => "en";
  public string TimeZone => timeZone;
  public IReadOnlySet<string> Permissions => new HashSet<string>();
  public bool Can(string permission) => false;
  public string? Claim(string type) => null;
}
