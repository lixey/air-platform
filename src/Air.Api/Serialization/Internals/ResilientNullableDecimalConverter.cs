namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientNullableDecimalConverter : JsonConverter<decimal?>
{
  public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    switch (reader.TokenType)
    {
      case JsonTokenType.Null:
        return null;

      case JsonTokenType.Number:
        if (reader.TryGetDecimal(out var val))
        {
          return val;
        }

        if (reader.TryGetDouble(out var dbl))
        {
          return (decimal)dbl;
        }

        throw new JsonException("Invalid numeric value.");

      case JsonTokenType.String:
        var str = reader.GetString()?.Trim();
        if (string.IsNullOrEmpty(str))
        {
          return null;
        }

        var normalized = str.Replace(" ", "").Replace("\u00A0", "").Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
          return parsed;
        }

        throw new JsonException($"Cannot convert '{str}' to decimal?.");

      default:
        throw new JsonException($"Unexpected token {reader.TokenType} when parsing decimal?.");
    }
  }

  public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
  {
    if (value.HasValue)
    {
      writer.WriteNumberValue(value.Value);
    }
    else
    {
      writer.WriteNullValue();
    }
  }
}
