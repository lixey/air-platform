using System.Globalization;

namespace Platform.Data.Internals;

internal static class RequestValue
{
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

  public static object? Convert(object? raw, Type targetType, string field)
  {
    var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
    if (raw is JsonElement { ValueKind: JsonValueKind.Object or JsonValueKind.Array })
    {
      throw Invalid(field, underlying);
    }

    var text = TextOf(raw);
    if (text is null)
    {
      return !targetType.IsValueType || underlying != targetType
        ? null
        : throw Invalid(field, underlying);
    }

    return TryParse(text, underlying, out var value) ? value : throw Invalid(field, underlying);
  }

  public static bool TryConvert(string text, Type targetType, out object? value) =>
    TryParse(text, Nullable.GetUnderlyingType(targetType) ?? targetType, out value);

  public static bool IsNull(object? raw) => raw is null or JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined };

  private static bool TryParse(string text, Type underlying, out object? value)
  {
    value = null;
    try
    {
      if (underlying.IsEnum)
      {
        return TryParseEnum(text, underlying, out value);
      }

      if (FieldKinds.ParserOf(underlying) is not { } parser)
      {
        return false;
      }

      value = parser(text);
      return true;
    }
    catch (Exception exception) when (exception is FormatException or OverflowException)
    {
      return false;
    }
  }

  private static bool TryParseEnum(string text, Type type, out object? value)
  {
    value = null;
    if (Enum.GetNames(type).FirstOrDefault(n => n.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } name)
    {
      value = Enum.Parse(type, name);
      return true;
    }

    var underlying = Enum.GetUnderlyingType(type);
    if (underlying == typeof(ulong))
    {
      if (!ulong.TryParse(text, NumberStyles.Integer, Invariant, out var u64))
      {
        return false;
      }

      value = Enum.ToObject(type, u64);
      return Enum.IsDefined(type, value);
    }

    if (!long.TryParse(text, NumberStyles.Integer, Invariant, out var number) || !FitsEnumUnderlying(underlying, number))
    {
      return false;
    }

    value = Enum.ToObject(type, number);
    return Enum.IsDefined(type, value);
  }

  private static bool FitsEnumUnderlying(Type underlying, long number) =>
    Type.GetTypeCode(underlying) switch
    {
      TypeCode.SByte => number is >= sbyte.MinValue and <= sbyte.MaxValue,
      TypeCode.Byte => number is >= byte.MinValue and <= byte.MaxValue,
      TypeCode.Int16 => number is >= short.MinValue and <= short.MaxValue,
      TypeCode.UInt16 => number is >= ushort.MinValue and <= ushort.MaxValue,
      TypeCode.Int32 => number is >= int.MinValue and <= int.MaxValue,
      TypeCode.UInt32 => number is >= uint.MinValue and <= uint.MaxValue,
      TypeCode.Int64 => true,
      _ => false
    };

  private static string? TextOf(object? raw) => raw switch
  {
    null => null,
    JsonElement json => json.ValueKind switch
    {
      JsonValueKind.Null or JsonValueKind.Undefined => null,
      JsonValueKind.String => json.GetString(),
      _ => json.GetRawText()
    },
    string text => text,
    DateTime or DateTimeOffset or DateOnly or TimeOnly => ((IFormattable)raw).ToString("O", Invariant),
    Enum => raw.ToString(),
    IFormattable formattable => formattable.ToString(null, Invariant),
    _ => raw.ToString()
  };

  private static QueryRequestException Invalid(string field, Type type) =>
    new(QueryError.InvalidValue, $"'{field}' takes a {type.Name} value.", field);
}
