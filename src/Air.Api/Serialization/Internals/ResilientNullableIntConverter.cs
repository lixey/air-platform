namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientNullableIntConverter : JsonConverter<int?>
{
  public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    switch (reader.TokenType)
    {
      case JsonTokenType.Null:
        return null;

      case JsonTokenType.Number:
        if (reader.TryGetInt32(out var val))
        {
          return val;
        }

        if (reader.TryGetDecimal(out var dec))
        {
          return (int)dec;
        }

        throw new JsonException("Invalid integer value.");

      case JsonTokenType.String:
        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
        {
          return null;
        }

        if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
          return parsed;
        }

        if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var decStr))
        {
          return (int)decStr;
        }

        throw new JsonException($"Cannot convert '{str}' to int?.");

      default:
        throw new JsonException($"Unexpected token {reader.TokenType} when parsing int?.");
    }
  }

  public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
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
