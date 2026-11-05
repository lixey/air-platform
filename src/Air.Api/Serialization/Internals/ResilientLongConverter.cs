namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientLongConverter : JsonConverter<long>
{
  public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return 0L;
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      var str = reader.GetString();
      if (string.IsNullOrWhiteSpace(str))
      {
        return 0L;
      }

      return long.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
        ? val
        : throw new JsonException($"Unable to convert \"{str}\" to {nameof(Int64)}.");
    }

    return reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var num)
      ? num
      : throw new JsonException($"Unable to convert {reader.TokenType} to {nameof(Int64)}.");
  }
  public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
