namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientNullableLongConverter : JsonConverter<long?>
{
  public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    switch (reader.TokenType)
    {
      case JsonTokenType.Null:
        return null;

      case JsonTokenType.Number:
        if (reader.TryGetInt64(out var val))
        {
          return val;
        }

        if (reader.TryGetDecimal(out var dec))
        {
          return (long)dec;
        }

        throw new JsonException("Invalid integer value.");

      case JsonTokenType.String:
        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
        {
          return null;
        }

        if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
          return parsed;
        }

        if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var decStr))
        {
          return (long)decStr;
        }

        throw new JsonException($"Cannot convert '{str}' to long?.");

      default:
        throw new JsonException($"Unexpected token {reader.TokenType} when parsing long?.");
    }
  }

  public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
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
