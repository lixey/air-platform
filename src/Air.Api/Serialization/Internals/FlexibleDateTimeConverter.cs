namespace Platform.Api.Serialization.Internals;

internal sealed class FlexibleDateTimeConverter : JsonConverter<DateTime>
{
  public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      throw new JsonException($"Cannot convert null to non-nullable {nameof(DateTime)}.");
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      return FlexibleDateTimeParser.Parse(reader.GetString() ?? string.Empty);
    }

    throw new JsonException($"Unable to convert {reader.TokenType} to {nameof(DateTime)}.");
  }

  public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
    FlexibleDateTimeParser.Write(writer, value);
}
