namespace Platform.App.Report;

public readonly struct ReportFilters(IReadOnlyDictionary<string, string>? values)
{
  public string? Get(string key) =>
    values != null && values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
      ? raw.Trim()
      : null;

  public string Choice(string key, string fallback, params ReadOnlySpan<string> allowed)
  {
    var raw = Get(key);
    if (raw is null)
    {
      return fallback;
    }

    foreach (var candidate in allowed)
    {
      if (string.Equals(candidate, raw, StringComparison.OrdinalIgnoreCase))
      {
        return candidate;
      }
    }

    return fallback;
  }

  public string Choice(string key, string fallback, IEnumerable<string> allowed)
  {
    var raw = Get(key);
    if (raw is null)
    {
      return fallback;
    }

    foreach (var candidate in allowed)
    {
      if (string.Equals(candidate, raw, StringComparison.OrdinalIgnoreCase))
      {
        return candidate;
      }
    }

    return fallback;
  }

  public DateOnly? Date(string key) =>
    Get(key) is { } raw && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
      ? parsed
      : null;

  public long? Id(string key) =>
    Get(key) is { } raw && long.TryParse(raw, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
      ? parsed
      : null;

  public bool Flag(string key, bool fallback = false) =>
    Get(key) is { } raw && bool.TryParse(raw, out var parsed)
      ? parsed
      : fallback;
}
