namespace Platform.Api.Serialization.Internals;

internal sealed class ResilientDecimalConverter : JsonConverter<decimal>
{
  public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return 0m;
    }

    if (reader.TokenType == JsonTokenType.String)
    {
      var str = reader.GetString();
      if (string.IsNullOrWhiteSpace(str))
      {
        return 0m;
      }

      return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
        ? val
        : throw new JsonException($"Unable to convert \"{str}\" to {nameof(Decimal)}.");
    }

    return reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var num)
      ? num
      : throw new JsonException($"Unable to convert {reader.TokenType} to {nameof(Decimal)}.");
  }
  public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
