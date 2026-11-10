namespace Platform.Api;

public static class TimeProviderExtensions
{
  extension(TimeProvider time)
  {
    /// <summary>Current UTC timestamp from the time provider.</summary>
    public DateTimeOffset UtcNow
    {
      get
      {
        ArgumentNullException.ThrowIfNull(time);
        return time.GetUtcNow();
      }
    }

    /// <summary>Current timestamp in the time provider's local (user/tenant) time zone.</summary>
    public DateTimeOffset LocalNow
    {
      get
      {
        ArgumentNullException.ThrowIfNull(time);
        return time.GetLocalNow();
      }
    }

    /// <summary>Current calendar date in the time provider's local (user/tenant) time zone.</summary>
    public DateOnly Today
    {
      get
      {
        ArgumentNullException.ThrowIfNull(time);
        return DateOnly.FromDateTime(time.GetLocalNow().DateTime);
      }
    }
  }
}
