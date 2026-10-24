namespace Platform.Api.Contracts;

[UiContract]
public static class MutationOutcome
{
  public const string Committed = "committed";
  public const string Refused = "refused";
  public const string RolledBack = "rolledBack";
  public const string Conflict = "conflict";
  public const string Unknown = "unknown";
}
