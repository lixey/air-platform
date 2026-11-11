namespace Platform.App.Internals;

internal static class FilterNodeEvaluator
{
  private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

  public static bool Evaluate(FilterNode node, object? target) =>
    Evaluate(node, field => PropertyAccessorCache.GetPathValue(target, field));

  public static bool Evaluate(FilterNode node, object? primary, object? fallback) =>
    Evaluate(node, field =>
    {
      if (primary is IDictionary<string, object?> dict)
      {
        var fromDict = PropertyAccessorCache.GetPathValue(dict, field);
        if (fromDict is not null || dict.ContainsKey(field))
        {
          return fromDict;
        }
      }
      else if (primary != null && HasFieldPath(primary.GetType(), field))
      {
        return PropertyAccessorCache.GetPathValue(primary, field);
      }

      return fallback != null ? PropertyAccessorCache.GetPathValue(fallback, field) : null;
    });

  private static bool HasFieldPath(Type rootType, string path)
  {
    var cur = rootType;
    foreach (var seg in PropertyAccessorCache.GetPathSegments(path))
    {
      var prop = PropertyAccessorCache.GetProperty(cur, seg);
      if (prop is null)
      {
        return false;
      }

      cur = prop.PropertyType;
    }

    return true;
  }

  public static bool Evaluate(FilterNode node, Func<string, object?> valueResolver)
  {
    if (node is FilterGroup group)
    {
      if (group.Items.Count == 0)
      {
        return true;
      }

      return FilterLogic.IsOr(group.Logic)
        ? group.Items.Any(i => Evaluate(i, valueResolver))
        : group.Items.All(i => Evaluate(i, valueResolver));
    }

    if (node is not FilterRule rule || string.IsNullOrEmpty(rule.Field))
    {
      return false;
    }

    var val = UnwrapJsonValue(valueResolver(rule.Field));
    var expected = UnwrapJsonValue(rule.Value);
    var op = rule.Operator;

    if (string.Equals(op, FilterOperator.IsNull, StringComparison.OrdinalIgnoreCase))
    {
      return IsEmptyValue(val);
    }

    if (string.Equals(op, FilterOperator.IsNotNull, StringComparison.OrdinalIgnoreCase))
    {
      return !IsEmptyValue(val);
    }

    if (string.Equals(op, FilterOperator.In, StringComparison.OrdinalIgnoreCase))
    {
      return MatchesIn(val, expected);
    }

    if (string.Equals(op, FilterOperator.NotIn, StringComparison.OrdinalIgnoreCase))
    {
      return !MatchesIn(val, expected);
    }

    if (string.Equals(op, FilterOperator.Contains, StringComparison.OrdinalIgnoreCase))
    {
      return MatchesContains(val, expected);
    }

    if (string.Equals(op, FilterOperator.NotContains, StringComparison.OrdinalIgnoreCase))
    {
      return !MatchesContains(val, expected);
    }

    if (string.Equals(op, FilterOperator.StartsWith, StringComparison.OrdinalIgnoreCase))
    {
      return val is string s && expected is string prefix && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    if (string.Equals(op, FilterOperator.EndsWith, StringComparison.OrdinalIgnoreCase))
    {
      return val is string s && expected is string suffix && s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    if (string.Equals(op, FilterOperator.Matches, StringComparison.OrdinalIgnoreCase))
    {
      return MatchesRegex(val, expected);
    }

    if (string.Equals(op, FilterOperator.NotMatches, StringComparison.OrdinalIgnoreCase))
    {
      return !MatchesRegex(val, expected);
    }

    if (string.Equals(op, FilterOperator.Equals, StringComparison.OrdinalIgnoreCase))
    {
      return AreEqualValues(val, expected);
    }

    if (string.Equals(op, FilterOperator.NotEquals, StringComparison.OrdinalIgnoreCase))
    {
      return !AreEqualValues(val, expected);
    }

    if (IsEmptyValue(val) || IsEmptyValue(expected))
    {
      return false;
    }

    var cmp = CompareValues(val, expected);
    return op switch
    {
      FilterOperator.GreaterThan => cmp is > 0,
      FilterOperator.GreaterThanOrEqual => cmp is >= 0,
      FilterOperator.LessThan => cmp is < 0,
      FilterOperator.LessThanOrEqual => cmp is <= 0,
      _ => false
    };
  }

  private static bool IsEmptyValue(object? val) => val switch
  {
    null => true,
    string s => s.Length == 0,
    IEnumerable seq => !seq.Cast<object?>().Any(),
    _ => false
  };

  private static bool MatchesIn(object? actual, object? expected)
  {
    if (actual is null || expected is not IEnumerable expectedSeq || expected is string)
    {
      return false;
    }

    var expectedList = expectedSeq.Cast<object?>().Select(UnwrapJsonValue).ToList();
    if (actual is IEnumerable actualSeq and not string)
    {
      return actualSeq.Cast<object?>().Any(item => expectedList.Any(exp => AreEqualValues(UnwrapJsonValue(item), exp)));
    }

    return expectedList.Any(exp => AreEqualValues(actual, exp));
  }

  private static bool MatchesContains(object? actual, object? expected)
  {
    if (actual is null || expected is null)
    {
      return false;
    }

    if (actual is string s && expected is string sub)
    {
      return s.Contains(sub, StringComparison.OrdinalIgnoreCase);
    }

    if (actual is IEnumerable actualSeq and not string)
    {
      return actualSeq.Cast<object?>().Any(item => AreEqualValues(UnwrapJsonValue(item), expected));
    }

    return false;
  }

  private static bool MatchesRegex(object? actual, object? expected)
  {
    if (actual is not string input || expected is not string pattern || pattern.Length == 0)
    {
      return false;
    }

    try
    {
      return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    }
    catch
    {
      return false;
    }
  }

  private static bool AreEqualValues(object? left, object? right)
  {
    left = UnwrapJsonValue(left);
    right = UnwrapJsonValue(right);

    if (left is null && right is null)
    {
      return true;
    }

    if (left is null || right is null)
    {
      return false;
    }

    if (left is bool lb && right is bool rb)
    {
      return lb == rb;
    }

    if (left is bool || right is bool)
    {
      return bool.TryParse(left.ToString(), out var parsedLeft) &&
             bool.TryParse(right.ToString(), out var parsedRight) &&
             parsedLeft == parsedRight;
    }

    return CompareValues(left, right) == 0;
  }

  public static Dictionary<string, object?> ExtractEqualityValues(FilterNode? node)
  {
    var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    ExtractEqualityValues(node, map);
    return map;
  }

  public static void ExtractEqualityValues(FilterNode? node, IDictionary<string, object?> target)
  {
    if (node is null)
    {
      return;
    }

    if (node is FilterRule rule)
    {
      if (string.Equals(rule.Operator, FilterOperator.Equals, StringComparison.OrdinalIgnoreCase) &&
          !string.IsNullOrWhiteSpace(rule.Field) &&
          rule.Value is not null)
      {
        target[rule.Field] = UnwrapJsonValue(rule.Value);
      }

      return;
    }

    if (node is FilterGroup group && !FilterLogic.IsOr(group.Logic))
    {
      foreach (var item in group.Items)
      {
        ExtractEqualityValues(item, target);
      }
    }
  }

  public static int? CompareValues(object? left, object? right)
  {
    left = UnwrapJsonValue(left);
    right = UnwrapJsonValue(right);

    if (left is null && right is null)
    {
      return 0;
    }

    if (left is null || right is null)
    {
      return null;
    }

    if (left.GetType().IsEnum)
    {
      left = Convert.ChangeType(left, Enum.GetUnderlyingType(left.GetType()), CultureInfo.InvariantCulture);
    }

    if (right.GetType().IsEnum)
    {
      right = Convert.ChangeType(right, Enum.GetUnderlyingType(right.GetType()), CultureInfo.InvariantCulture);
    }

    if (IsNumericRuntimeType(left) || IsNumericRuntimeType(right))
    {
      return TryConvertToDecimal(left, out var leftDec) && TryConvertToDecimal(right, out var rightDec)
        ? leftDec.CompareTo(rightDec)
        : null;
    }

    if (TryParseTemporalTicks(left, out var leftTicks) && TryParseTemporalTicks(right, out var rightTicks))
    {
      return leftTicks.CompareTo(rightTicks);
    }

    if (left is bool lb && right is bool rb)
    {
      return lb.CompareTo(rb);
    }

    return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsNumericRuntimeType(object value) =>
    value is decimal or int or long or short or byte or sbyte or ushort or uint or ulong or double or float;

  private static bool TryParseTemporalTicks(object value, out long utcTicks)
  {
    switch (value)
    {
      case DateOnly d:
        utcTicks = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Ticks;
        return true;
      case DateTime dt:
        utcTicks = (dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc)).Ticks;
        return true;
      case DateTimeOffset dto:
        utcTicks = dto.UtcTicks;
        return true;
      case string s when s.Length >= 10 && char.IsDigit(s[0]) && char.IsDigit(s[1]) && char.IsDigit(s[2]) && char.IsDigit(s[3]) && s[4] == '-':
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
          utcTicks = parsedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Ticks;
          return true;
        }

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDto))
        {
          utcTicks = parsedDto.UtcTicks;
          return true;
        }

        break;
    }

    utcTicks = 0;
    return false;
  }

  public static bool TryConvertToDecimal(object? value, out decimal result)
  {
    switch (value)
    {
      case null:
        result = 0m;
        return false;
      case decimal d: result = d; return true;
      case int i: result = i; return true;
      case long l: result = l; return true;
      case short s: result = s; return true;
      case byte b: result = b; return true;
      case sbyte sb: result = sb; return true;
      case ushort us: result = us; return true;
      case uint ui: result = ui; return true;
      case ulong ul: result = ul; return true;
      case Enum enumVal:
        result = Convert.ToDecimal(enumVal, CultureInfo.InvariantCulture);
        return true;
      case double dbVal and not double.NaN and not double.PositiveInfinity and not double.NegativeInfinity:
        result = (decimal)dbVal; return true;
      case float flVal and not float.NaN and not float.PositiveInfinity and not float.NegativeInfinity:
        result = (decimal)flVal; return true;
      case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetDecimal(out result):
        return true;
      case JsonElement { ValueKind: JsonValueKind.Number } jeDbl when jeDbl.TryGetDouble(out var jd) && !double.IsNaN(jd) && !double.IsInfinity(jd):
        result = (decimal)jd; return true;
      case JsonElement { ValueKind: JsonValueKind.String } jeStr when decimal.TryParse(jeStr.GetString(), NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result):
        return true;
      case string strVal when decimal.TryParse(strVal, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result):
        return true;
      default:
        result = 0m; return false;
    }
  }

  private static object? UnwrapJsonValue(object? val) => val is JsonElement je
    ? je.ValueKind switch
    {
      JsonValueKind.Null or JsonValueKind.Undefined => null,
      JsonValueKind.True => true,
      JsonValueKind.False => false,
      JsonValueKind.Number when je.TryGetInt64(out var l) => l,
      JsonValueKind.Number when je.TryGetDecimal(out var d) => d,
      JsonValueKind.String => je.GetString(),
      JsonValueKind.Array => je.EnumerateArray().Select(e => UnwrapJsonValue(e)).ToList(),
      _ => je.ToString()
    }
    : val;
}
