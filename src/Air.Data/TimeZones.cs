namespace Air.Data;

/// <summary>
/// The time zones of the platform: a zone is its IANA id, of at most 64 characters.
/// </summary>
public static class TimeZones
{
  public const int MaxLength = 64;

  private static readonly Lazy<IReadOnlyList<string>> OfferedIds = new(Load);

  public static IReadOnlyList<string> Offered => OfferedIds.Value;

  public static bool TryFind(string? id, out TimeZoneInfo zone)
  {
    zone = TimeZoneInfo.Utc;
    if (string.IsNullOrEmpty(id) || id.Length > MaxLength)
    {
      return false;
    }

    if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) || !found.HasIanaId)
    {
      return false;
    }

    zone = found;
    return true;
  }

  private static IReadOnlyList<string> Load()
  {
    var ids = new SortedSet<string>(StringComparer.Ordinal) { "UTC" };
    foreach (var system in TimeZoneInfo.GetSystemTimeZones())
    {
      if (IanaIdOf(system) is { } id && TryFind(id, out _))
      {
        ids.Add(id);
      }
    }

    return [.. ids];
  }

  private static string? IanaIdOf(TimeZoneInfo system)
  {
    if (system.HasIanaId)
    {
      return system.Id;
    }

    return TimeZoneInfo.TryConvertWindowsIdToIanaId(system.Id, out var iana) ? iana : null;
  }
}
