using System.Collections.Frozen;

namespace Platform.Data.Internals;

internal static class OperatorSets
{
  private static readonly string[] Identity =
  [
    FilterOperator.Equals, FilterOperator.NotEquals, FilterOperator.In, FilterOperator.NotIn,
    FilterOperator.IsNull, FilterOperator.IsNotNull
  ];

  private static readonly string[] Order =
  [
    FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual, FilterOperator.LessThan, FilterOperator.LessThanOrEqual
  ];

  private static readonly FrozenSet<string> Identifying = Identity.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
  private static readonly FrozenSet<string> Ordered = Identity.Concat(Order).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

  public static IReadOnlySet<string> For(FieldKind kind) => kind switch
  {
    FieldKind.Text => FilterOperator.All,
    FieldKind.Number or FieldKind.Temporal => Ordered,
    _ => Identifying
  };

  public static IReadOnlySet<string> ForCollection() => Identifying;
}
