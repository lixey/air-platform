namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientDateTimeConverter : JsonConverter<DateTime>
{
  public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return DateTime.MinValue;
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      var str = reader.GetString();
      if (string.IsNullOrWhiteSpace(str))
      {
        return DateTime.MinValue;
      }

      if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
      {
        return DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified);
      }

      if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
      {
        return DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
      }

      if (reader.TryGetDateTime(out var isoDt))
      {
        return DateTime.SpecifyKind(isoDt, DateTimeKind.Unspecified);
      }

      throw new JsonException($"Unable to convert \"{str}\" to {nameof(DateTime)}.");
    }

    if (reader.TryGetDateTime(out var tokenDt))
    {
      return DateTime.SpecifyKind(tokenDt, DateTimeKind.Unspecified);
    }

    throw new JsonException($"Unable to convert {reader.TokenType} to {nameof(DateTime)}.");
  }
  public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
    writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Unspecified).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
}
