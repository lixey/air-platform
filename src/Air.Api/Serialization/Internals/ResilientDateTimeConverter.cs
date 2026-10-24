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
      return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    return DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);
  }
  public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
    writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o"));
}
