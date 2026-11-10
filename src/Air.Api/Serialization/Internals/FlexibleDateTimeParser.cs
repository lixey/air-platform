namespace Platform.Api.Serialization.Internals;

internal static class FlexibleDateTimeParser
{
  public static DateTime Parse(string raw)
  {
    var trimmed = raw.AsSpan().Trim();
    if (trimmed.IsEmpty)
    {
      throw new JsonException("Cannot convert empty string to DateTime.");
    }

    if (HasExplicitOffset(trimmed))
    {
      if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
      {
        return dto.UtcDateTime;
      }

      throw new JsonException($"Unable to convert \"{raw}\" to {nameof(DateTime)}.");
    }

    if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
    {
      return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
    }

    throw new JsonException($"Unable to convert \"{raw}\" to {nameof(DateTime)}.");
  }

  public static void Write(Utf8JsonWriter writer, DateTime value)
  {
    if (value.Kind == DateTimeKind.Utc)
    {
      writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
      return;
    }

    if (value.Kind == DateTimeKind.Local)
    {
      writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
      return;
    }

    writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
  }

  private static bool HasExplicitOffset(ReadOnlySpan<char> value)
  {
    if (value[^1] is 'Z' or 'z')
    {
      return true;
    }

    var timeSeparatorIndex = value.IndexOfAny('T', 't', ' ');
    if (timeSeparatorIndex < 0 || timeSeparatorIndex + 1 >= value.Length)
    {
      return false;
    }

    var timePart = value[(timeSeparatorIndex + 1)..];
    return timePart.IndexOfAny('+', '-') >= 0;
  }
}
