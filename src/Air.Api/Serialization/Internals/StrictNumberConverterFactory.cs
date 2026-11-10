namespace Platform.Api.Serialization.Internals;

internal sealed class StrictNumberConverterFactory : JsonConverterFactory
{
  public override bool CanConvert(Type typeToConvert) =>
    typeToConvert == typeof(int) ||
    typeToConvert == typeof(int?) ||
    typeToConvert == typeof(long) ||
    typeToConvert == typeof(long?) ||
    typeToConvert == typeof(decimal) ||
    typeToConvert == typeof(decimal?);

  public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
  {
    if (typeToConvert == typeof(int))
    {
      return new StrictValueNumberConverter<int>();
    }

    if (typeToConvert == typeof(int?))
    {
      return new StrictNullableNumberConverter<int>();
    }

    if (typeToConvert == typeof(long))
    {
      return new StrictValueNumberConverter<long>();
    }

    if (typeToConvert == typeof(long?))
    {
      return new StrictNullableNumberConverter<long>();
    }

    if (typeToConvert == typeof(decimal))
    {
      return new StrictValueNumberConverter<decimal>();
    }

    if (typeToConvert == typeof(decimal?))
    {
      return new StrictNullableNumberConverter<decimal>();
    }

    throw new NotSupportedException($"Unsupported type {typeToConvert}.");
  }
}
