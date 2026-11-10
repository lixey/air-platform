namespace Platform.Api.Serialization.Internals;

internal sealed class StrictValueNumberConverter<T> : JsonConverter<T>
  where T : struct
{
  public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      throw new JsonException($"Cannot convert null to non-nullable {typeof(T).Name}.");
    }

    if (typeof(T) == typeof(int))
    {
      var v = StrictNumberParser.ParseInt32(ref reader);
      return (T)(object)v;
    }

    if (typeof(T) == typeof(long))
    {
      var v = StrictNumberParser.ParseInt64(ref reader);
      return (T)(object)v;
    }

    if (typeof(T) == typeof(decimal))
    {
      var v = StrictNumberParser.ParseDecimal(ref reader);
      return (T)(object)v;
    }

    throw new NotSupportedException($"Unsupported numeric type {typeof(T)}.");
  }

  public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
  {
    switch (value)
    {
      case int i:
        writer.WriteNumberValue(i);
        break;
      case long l:
        writer.WriteNumberValue(l);
        break;
      case decimal d:
        writer.WriteNumberValue(d);
        break;
      default:
        throw new NotSupportedException($"Unsupported numeric type {typeof(T)}.");
    }
  }
}
