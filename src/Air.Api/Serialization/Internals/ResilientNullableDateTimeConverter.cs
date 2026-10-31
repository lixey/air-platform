namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientNullableDateTimeConverter : JsonConverter<DateTime?>
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

      if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
      {
        return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
      }
    }

    return DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Unspecified);
  }
  public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
  {
    if (value is null)
    {
      writer.WriteNullValue();
    }
    else
    {
      writer.WriteStringValue(DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
    }
  }
}
