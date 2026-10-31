namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientDateTimeConverter : JsonConverter<DateTime>
{
  public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return DateTime.MinValue;
    }

    if (reader.TokenType == JsonTokenType.String && DateTime.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
    {
      return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
    }

    return DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Unspecified);
  }
  public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
    writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Unspecified).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
}
