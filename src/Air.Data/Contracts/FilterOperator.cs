using System.Collections.Frozen;

namespace Platform.Data.Contracts;

public static class FilterOperator
{
  public new const string Equals = "equals";
  public const string NotEquals = "notEquals";
  public const string GreaterThan = "greaterThan";
  public const string GreaterThanOrEqual = "greaterThanOrEqual";
  public const string LessThan = "lessThan";
  public const string LessThanOrEqual = "lessThanOrEqual";

  public const string In = "in";
  public const string NotIn = "notIn";
  public const string IsNull = "isNull";
  public const string IsNotNull = "isNotNull";

  public const string Contains = "contains";
  public const string NotContains = "notContains";
  public const string StartsWith = "startsWith";
  public const string NotStartsWith = "notStartsWith";
  public const string EndsWith = "endsWith";
  public const string NotEndsWith = "notEndsWith";

  public const string Matches = "matches";
  public const string NotMatches = "notMatches";

  public static readonly FrozenSet<string> All = new[]
  {
    Equals, NotEquals,
    GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
    In, NotIn, IsNull, IsNotNull,
    Contains, NotContains, StartsWith, NotStartsWith, EndsWith, NotEndsWith,
    Matches, NotMatches
  }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

  public static bool IsValid(string? op) => op != null && All.Contains(op);
}
