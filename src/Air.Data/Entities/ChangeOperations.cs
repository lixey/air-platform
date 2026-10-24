namespace Platform.Data.Entities;

/// <summary>
/// What happened to a row the change journal records (AppPlatformChange.Operation).
/// </summary>
public static class ChangeOperations
{
  public const string Added = nameof(Added);
  public const string Modified = nameof(Modified);
  public const string Deleted = nameof(Deleted);
  public const string Restored = nameof(Restored);
}
