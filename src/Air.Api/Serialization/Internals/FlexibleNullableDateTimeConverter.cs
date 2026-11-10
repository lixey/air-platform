namespace Platform.Api.Serialization.Internals;

internal sealed class FlexibleNullableDateTimeConverter : JsonConverter<DateTime?>
{
  public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return null;
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      var str = reader.GetString();
      if (string.IsNullOrWhiteSpace(str))
      {
        return null;
      }

      return FlexibleDateTimeParser.Parse(str);
    }

    throw new JsonException($"Unable to convert {reader.TokenType} to {nameof(DateTime)}?.");
  }

  public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
  {
    if (value.HasValue)
    {
      FlexibleDateTimeParser.Write(writer, value.Value);
    }
    else
    {
      writer.WriteNullValue();
    }
  }
}
