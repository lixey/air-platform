namespace DemoEcom.Migrations.Seeds;

internal static class EcomSeedClock
{
  public const int Days = 30;
  public const int RecentDays = 7;
  private const int FirstHour = 9;
  private const int LastHour = 17;

  public static DateOnly Today(TimeZoneInfo zone, DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

  public static DateTimeOffset At(TimeZoneInfo zone, DateOnly date, Randomizer random)
  {
    var local = date.ToDateTime(new TimeOnly(random.Int(FirstHour, LastHour), random.Int(0, 59)));
    return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
  }

  public static DateTimeOffset Opening(TimeZoneInfo zone, DateOnly date)
  {
    var local = date.ToDateTime(new TimeOnly(FirstHour, 0));
    return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
  }

  public static TimeZoneInfo ZoneOf(AppSeedTenant tenant, IReadOnlyList<AppSeedTenant> tenants)
  {
    var row = tenant.Row;
    for (var depth = 0; depth <= tenants.Count; depth++)
    {
      if (TimeZones.TryFind(row.TimeZone, out var zone))
      {
        return zone;
      }

      if (tenants.FirstOrDefault(parent => parent.Row.Id == row.ParentId) is not { } above)
      {
        break;
      }

      row = above.Row;
    }

    return TimeZoneInfo.Utc;
  }
}
