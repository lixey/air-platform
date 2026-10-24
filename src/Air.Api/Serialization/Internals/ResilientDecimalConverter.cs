namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientDecimalConverter : JsonConverter<decimal>
{
  public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return 0m;
    }

    if (reader.TokenType == JsonTokenType.String && decimal.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
    {
      return val;
    }

    return reader.GetDecimal();
  }
  public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
