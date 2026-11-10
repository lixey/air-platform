namespace Platform.Api.Serialization.Internals;

internal sealed class StrictNullableNumberConverter<T> : JsonConverter<T?>
  where T : struct
{
  public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    if (reader.TokenType == JsonTokenType.Null)
    {
      return null;
    }

    if (reader.TokenType == JsonTokenType.String && string.IsNullOrWhiteSpace(reader.GetString()))
    {
      return null;
    }

    if (typeof(T) == typeof(int))
    {
      return (T)(object)StrictNumberParser.ParseInt32(ref reader);
    }

    if (typeof(T) == typeof(long))
    {
      return (T)(object)StrictNumberParser.ParseInt64(ref reader);
    }

    if (typeof(T) == typeof(decimal))
    {
      return (T)(object)StrictNumberParser.ParseDecimal(ref reader);
    }

    throw new NotSupportedException($"Unsupported numeric type {typeof(T)}.");
  }

  public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
  {
    if (!value.HasValue)
    {
      writer.WriteNullValue();
      return;
    }

    switch (value.Value)
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
