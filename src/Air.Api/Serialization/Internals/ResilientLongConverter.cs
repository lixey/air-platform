namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientLongConverter : JsonConverter<long>
{
  public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return 0L;
    }

    if (reader.TokenType == JsonTokenType.String && long.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
    {
      return val;
    }

    return reader.GetInt64();
  }
  public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
