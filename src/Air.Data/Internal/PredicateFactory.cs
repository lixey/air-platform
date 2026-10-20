using System.Collections.Frozen;

namespace Air.Data.Internal;

internal static class PredicateFactory
{
  private static readonly MethodInfo StringCompare = typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!;
  private static readonly MethodInfo EnumerableContains = typeof(Enumerable).GetMethods()
    .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);
  private static readonly MethodInfo EnumerableAny = typeof(Enumerable).GetMethods()
    .Single(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 1);
  private static readonly MethodInfo EnumerableAnyWithPredicate = typeof(Enumerable).GetMethods()
    .Single(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2);
  private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
  private static readonly MethodInfo StringStartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
  private static readonly MethodInfo StringEndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
  private static readonly MethodInfo StringToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

  private static readonly FrozenDictionary<string, (string Positive, bool Negate)> Operators =
    new Dictionary<string, (string Positive, bool Negate)>
    {
      [FilterOperator.Equals] = (FilterOperator.Equals, false),
      [FilterOperator.NotEquals] = (FilterOperator.Equals, true),
      [FilterOperator.GreaterThan] = (FilterOperator.GreaterThan, false),
      [FilterOperator.GreaterThanOrEqual] = (FilterOperator.GreaterThanOrEqual, false),
      [FilterOperator.LessThan] = (FilterOperator.LessThan, false),
      [FilterOperator.LessThanOrEqual] = (FilterOperator.LessThanOrEqual, false),
      [FilterOperator.In] = (FilterOperator.In, false),
      [FilterOperator.NotIn] = (FilterOperator.In, true),
      [FilterOperator.IsNull] = (FilterOperator.IsNull, false),
      [FilterOperator.IsNotNull] = (FilterOperator.IsNull, true),
      [FilterOperator.Contains] = (FilterOperator.Contains, false),
      [FilterOperator.NotContains] = (FilterOperator.Contains, true),
      [FilterOperator.StartsWith] = (FilterOperator.StartsWith, false),
      [FilterOperator.NotStartsWith] = (FilterOperator.StartsWith, true),
      [FilterOperator.EndsWith] = (FilterOperator.EndsWith, false),
      [FilterOperator.NotEndsWith] = (FilterOperator.EndsWith, true),
      [FilterOperator.Matches] = (FilterOperator.Matches, false),
      [FilterOperator.NotMatches] = (FilterOperator.Matches, true)
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

  public static Expression Build(QueryField field, Expression member, FilterRule rule, QueryLimits limits)
  {
    var (positive, negate) = Operators[rule.Operator];
    var predicate = positive switch
    {
      FilterOperator.IsNull => IsNull(field, member),
      FilterOperator.Equals => Equality(field, member, rule.Value),
      FilterOperator.In => Membership(field, member, rule, limits),
      FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual
        or FilterOperator.LessThan or FilterOperator.LessThanOrEqual => Relational(field, member, positive, rule.Value),
      _ => Text(field, member, positive, rule.Value)
    };

    return negate ? Expression.Not(predicate) : predicate;
  }

  internal static Expression IsNull(QueryField field, Expression member)
  {
    if (field.IsCollection)
    {
      return Expression.Not(Expression.Call(EnumerableAny.MakeGenericMethod(field.ValueType), member));
    }

    if (!field.IsNullableInStore)
    {
      return Expression.Constant(false);
    }

    var nullable = Lift(member);
    return Expression.Equal(nullable, Expression.Constant(null, nullable.Type));
  }

  private static Expression Lift(Expression member) =>
    member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) is null
      ? Expression.Convert(member, typeof(Nullable<>).MakeGenericType(member.Type))
      : member;

  private static Expression Guarded(QueryField field, Expression member, Expression predicate) =>
    field.IsNullableInStore ? Expression.AndAlso(Expression.Not(IsNull(field, member)), predicate) : predicate;

  private static object Required(QueryField field, object? raw) =>
    RequestValue.Convert(raw, field.ValueType, field.Name)
    ?? throw new QueryRequestException(QueryError.InvalidValue, $"'{field.Name}' needs a value for this operator.", field.Name);

  internal static Expression Equality(QueryField field, Expression member, object? raw)
  {
    if (RequestValue.IsNull(raw))
    {
      return IsNull(field, member);
    }

    if (field.IsCollection)
    {
      return Expression.Call(
        EnumerableContains.MakeGenericMethod(field.ValueType),
        member,
        BoundValue.Of(Required(field, raw), field.ValueType));
    }

    return Guarded(field, member, Expression.Equal(member, BoundValue.Of(Required(field, raw), member.Type)));
  }

  private static Expression Relational(QueryField field, Expression member, string op, object? raw)
  {
    var value = BoundValue.Of(Required(field, raw), member.Type);

    var (left, right) = field.Kind == FieldKind.Text
      ? (Expression.Call(StringCompare, member, value), (Expression)Expression.Constant(0))
      : (member, value);

    var comparison = op switch
    {
      FilterOperator.GreaterThan => Expression.GreaterThan(left, right),
      FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(left, right),
      FilterOperator.LessThan => Expression.LessThan(left, right),
      _ => Expression.LessThanOrEqual(left, right)
    };
    return Guarded(field, member, comparison);
  }

  private static Expression Membership(QueryField field, Expression member, FilterRule rule, QueryLimits limits)
  {
    var raw = RawItems(field, rule).ToList();
    if (raw.Count > limits.MaxFilterValues)
    {
      throw new QueryRequestException(
        QueryError.TooManyValues, $"'{field.Name}' is given {raw.Count} values; {limits.MaxFilterValues} is the most.", field.Name);
    }

    if (raw.Count == 0)
    {
      return Expression.Constant(false);
    }

    var hasNull = raw.Any(RequestValue.IsNull);
    if (hasNull && !field.IsNullableInStore)
    {
      throw new QueryRequestException(
        QueryError.InvalidValue, $"'{field.Name}' is non-nullable and cannot contain null in an in filter.", field.Name);
    }

    var values = raw.Where(item => !RequestValue.IsNull(item)).ToList();
    if (values.Count == 0)
    {
      return IsNull(field, member);
    }

    if (field.IsCollection)
    {
      var elemType = field.ValueType;
      var colListType = typeof(List<>).MakeGenericType(elemType);
      var colList = (IList)Activator.CreateInstance(colListType)!;
      values.ForEach(item => colList.Add(Required(field, item)));
      var itemParam = Expression.Parameter(elemType, "item");
      var itemInList = Expression.Call(
        EnumerableContains.MakeGenericMethod(elemType),
        BoundValue.Of(colList, colListType),
        itemParam);
      var anyMatch = Expression.Call(
        EnumerableAnyWithPredicate.MakeGenericMethod(elemType),
        member,
        Expression.Lambda(itemInList, itemParam));
      return hasNull ? Expression.OrElse(anyMatch, IsNull(field, member)) : anyMatch;
    }

    var listType = typeof(List<>).MakeGenericType(member.Type);
    var list = (IList)Activator.CreateInstance(listType)!;
    values.ForEach(item => list.Add(Required(field, item)));
    var contains = Guarded(
      field, member, Expression.Call(EnumerableContains.MakeGenericMethod(member.Type), BoundValue.Of(list, listType), member));

    return hasNull ? Expression.OrElse(contains, IsNull(field, member)) : contains;
  }

  private static IEnumerable<object?> RawItems(QueryField field, FilterRule rule) => rule.Value switch
  {
    null or JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } =>
      throw new QueryRequestException(QueryError.InvalidValue, $"'{field.Name}' is matched against no set of values.", field.Name),
    JsonElement { ValueKind: JsonValueKind.Array } array => array.EnumerateArray().Select(item => (object?)item),
    string text => [text],
    IEnumerable items => items.Cast<object?>(),
    var single => [single]
  };

  private static Expression Text(QueryField field, Expression member, string op, object? raw)
  {
    var text = (string)Required(field, raw);
    var pattern = op switch
    {
      FilterOperator.Matches => TextPattern.Parse(text),
      FilterOperator.StartsWith => new TextPattern(TextPatternKind.StartsWith, text),
      FilterOperator.EndsWith => new TextPattern(TextPatternKind.EndsWith, text),
      _ => new TextPattern(TextPatternKind.Contains, text)
    };

    if (pattern.IsEmpty)
    {
      throw new QueryRequestException(QueryError.InvalidValue, $"'{field.Name}' is matched against an empty text.", field.Name);
    }

    return TextMatch(field, member, pattern);
  }

  internal static Expression TextMatch(QueryField field, Expression member, TextPattern pattern)
  {
    var loweredMember = Expression.Call(member, StringToLower);
    var term = BoundValue.Of(pattern.Term.ToLowerInvariant(), typeof(string));
    Expression match = pattern.Kind switch
    {
      TextPatternKind.Equals => Expression.Equal(loweredMember, term),
      TextPatternKind.StartsWith => Expression.Call(loweredMember, StringStartsWith, term),
      TextPatternKind.EndsWith => Expression.Call(loweredMember, StringEndsWith, term),
      _ => Expression.Call(loweredMember, StringContains, term)
    };
    return Guarded(field, member, match);
  }
}
