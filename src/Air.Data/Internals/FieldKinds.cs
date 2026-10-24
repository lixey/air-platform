using System.Collections.Frozen;
using System.Globalization;

namespace Platform.Data.Internals;

internal static class FieldKinds
{
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

  private static readonly FrozenDictionary<Type, (FieldKind Kind, Func<string, object> Parse)> Scalars =
    new Dictionary<Type, (FieldKind Kind, Func<string, object> Parse)>
    {
      [typeof(string)] = (FieldKind.Text, text => text),
      [typeof(bool)] = (FieldKind.Boolean, text => bool.Parse(text)),
      [typeof(Guid)] = (FieldKind.Guid, text => Guid.Parse(text)),
      [typeof(byte)] = (FieldKind.Number, text => byte.Parse(text, NumberStyles.Integer, Invariant)),
      [typeof(short)] = (FieldKind.Number, text => short.Parse(text, NumberStyles.Integer, Invariant)),
      [typeof(int)] = (FieldKind.Number, text => int.Parse(text, NumberStyles.Integer, Invariant)),
      [typeof(long)] = (FieldKind.Number, text => long.Parse(text, NumberStyles.Integer, Invariant)),
      [typeof(float)] = (FieldKind.Number, text => float.Parse(text, NumberStyles.Float, Invariant)),
      [typeof(double)] = (FieldKind.Number, text => double.Parse(text, NumberStyles.Float, Invariant)),
      [typeof(decimal)] = (FieldKind.Number, text => decimal.Parse(text, NumberStyles.Float, Invariant)),
      [typeof(DateTime)] = (FieldKind.Temporal, LocalDateTime),
      [typeof(DateTimeOffset)] = (FieldKind.Temporal,
        text => DateTimeOffset.Parse(text, Invariant, DateTimeStyles.AssumeUniversal).ToUniversalTime()),
      [typeof(DateOnly)] = (FieldKind.Temporal, text => DateOnly.Parse(text, Invariant)),
      [typeof(TimeOnly)] = (FieldKind.Temporal, text => TimeOnly.Parse(text, Invariant)),
      [typeof(TimeSpan)] = (FieldKind.Temporal, text => TimeSpan.Parse(text, Invariant))
    }.ToFrozenDictionary();

  public static FieldKind? Of(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying.IsEnum)
    {
      return FieldKind.Enum;
    }

    return Scalars.TryGetValue(underlying, out var scalar) ? scalar.Kind : null;
  }

  public static (FieldKind Kind, Type ElementType)? CollectionOf(Type type)
  {
    if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
    {
      return null;
    }

    var enumIface = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
      ? type
      : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
    if (enumIface is null)
    {
      return null;
    }

    var elementType = enumIface.GetGenericArguments()[0];
    return Of(elementType) is { } kind
      ? (kind, Nullable.GetUnderlyingType(elementType) ?? elementType)
      : null;
  }

  public static Func<string, object>? ParserOf(Type underlying) =>
    Scalars.TryGetValue(underlying, out var scalar) ? scalar.Parse : null;

  private static object LocalDateTime(string text)
  {
    var value = DateTime.Parse(text, Invariant, DateTimeStyles.RoundtripKind);
    return value.Kind == DateTimeKind.Unspecified ? value : throw new FormatException("A local date-time carries no zone or offset.");
  }
}
