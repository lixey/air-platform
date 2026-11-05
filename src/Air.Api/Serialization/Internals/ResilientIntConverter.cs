namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientIntConverter : JsonConverter<int>
{
  public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return 0;
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      var str = reader.GetString();
      if (string.IsNullOrWhiteSpace(str))
      {
        return 0;
      }

      return int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
        ? val
        : throw new JsonException($"Unable to convert \"{str}\" to {nameof(Int32)}.");
    }

    return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var num)
      ? num
      : throw new JsonException($"Unable to convert {reader.TokenType} to {nameof(Int32)}.");
  }
  public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
