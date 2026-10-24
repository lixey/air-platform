namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientIntConverter : JsonConverter<int>
{
  public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return 0;
    }

    if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
    {
      return val;
    }

    return reader.GetInt32();
  }
  public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
