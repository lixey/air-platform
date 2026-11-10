namespace Platform.Api.Serialization.Internals;

internal static class StrictNumberParser
{
  private const NumberStyles DecimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

  public static int ParseInt32(ref Utf8JsonReader reader)
  {
    if (reader.TokenType == JsonTokenType.Number)
    {
      if (reader.TryGetInt32(out var value))
      {
        return value;
      }

      if (reader.TryGetDecimal(out var dec) &&
          dec == decimal.Truncate(dec) &&
          dec is >= int.MinValue and <= int.MaxValue)
      {
        return (int)dec;
      }

      throw new JsonException("Numeric value is not a valid 32-bit integer.");
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      var raw = reader.GetString();
      var normalized = NormalizeNumberString(raw, allowDecimalSeparator: false);
      if (int.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
      {
        return parsed;
      }

      throw new JsonException($"Cannot convert \"{raw}\" to {nameof(Int32)}.");
    }

    throw new JsonException($"Unexpected token {reader.TokenType} when parsing {nameof(Int32)}.");
  }

  public static long ParseInt64(ref Utf8JsonReader reader)
  {
    if (reader.TokenType == JsonTokenType.Number)
    {
      if (reader.TryGetInt64(out var value))
      {
        return value;
      }

      if (reader.TryGetDecimal(out var dec) &&
          dec == decimal.Truncate(dec) &&
          dec is >= long.MinValue and <= long.MaxValue)
      {
        return (long)dec;
      }

      throw new JsonException("Numeric value is not a valid 64-bit integer.");
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      var raw = reader.GetString();
      var normalized = NormalizeNumberString(raw, allowDecimalSeparator: false);
      if (long.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
      {
        return parsed;
      }

      throw new JsonException($"Cannot convert \"{raw}\" to {nameof(Int64)}.");
    }

    throw new JsonException($"Unexpected token {reader.TokenType} when parsing {nameof(Int64)}.");
  }

  public static decimal ParseDecimal(ref Utf8JsonReader reader)
  {
    if (reader.TokenType == JsonTokenType.Number)
    {
      if (reader.TryGetDecimal(out var value))
      {
        return value;
      }

      if (reader.TryGetDouble(out var dbl) && double.IsFinite(dbl))
      {
        try
        {
          return checked((decimal)dbl);
        }
        catch (OverflowException ex)
        {
          throw new JsonException("Numeric value exceeds Decimal bounds.", ex);
        }
      }

      throw new JsonException("Numeric value is not a valid Decimal.");
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      var raw = reader.GetString();
      var normalized = NormalizeNumberString(raw, allowDecimalSeparator: true);
      if (decimal.TryParse(normalized, DecimalStyles, CultureInfo.InvariantCulture, out var parsed))
      {
        return parsed;
      }

      throw new JsonException($"Cannot convert \"{raw}\" to {nameof(Decimal)}.");
    }

    throw new JsonException($"Unexpected token {reader.TokenType} when parsing {nameof(Decimal)}.");
  }

  private static string NormalizeNumberString(string? raw, bool allowDecimalSeparator)
  {
    if (string.IsNullOrWhiteSpace(raw))
    {
      throw new JsonException("Empty string is not a valid number.");
    }

    var sb = new StringBuilder(raw.Length);
    var separatorCount = 0;

    foreach (var ch in raw.AsSpan().Trim())
    {
      if (ch is ' ' or '\u00A0' or '\u202F')
      {
        continue;
      }

      if (ch is ',' or '.')
      {
        if (!allowDecimalSeparator || ++separatorCount > 1)
        {
          throw new JsonException($"Invalid numeric format \"{raw}\".");
        }

        sb.Append('.');
        continue;
      }

      sb.Append(ch);
    }

    return sb.ToString();
  }
}
