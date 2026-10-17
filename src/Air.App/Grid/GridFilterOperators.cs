using Air.App.Localization;
using Air.Data.Contracts;

namespace Air.App.Grid;

/// <summary>
/// How every filter operator is shown: the text key of its label and its symbol.
/// </summary>
public static class GridFilterOperators
{
  public static readonly IReadOnlyList<GridFilterOperatorDto> All =
  [
    Operator(FilterOperator.Equals, "="),
    Operator(FilterOperator.NotEquals, "<>"),
    Operator(FilterOperator.GreaterThan, ">"),
    Operator(FilterOperator.GreaterThanOrEqual, ">="),
    Operator(FilterOperator.LessThan, "<"),
    Operator(FilterOperator.LessThanOrEqual, "<="),
    Operator(FilterOperator.In),
    Operator(FilterOperator.NotIn),
    Operator(FilterOperator.IsNull),
    Operator(FilterOperator.IsNotNull),
    Operator(FilterOperator.Contains),
    Operator(FilterOperator.NotContains),
    Operator(FilterOperator.StartsWith),
    Operator(FilterOperator.NotStartsWith),
    Operator(FilterOperator.EndsWith),
    Operator(FilterOperator.NotEndsWith),
    Operator(FilterOperator.Matches),
    Operator(FilterOperator.NotMatches)
  ];

  public static string TextKey(string handle) => "Operator" + char.ToUpperInvariant(handle[0]) + handle[1..];

  public static List<GridFilterOperatorDto> For(IReadOnlySet<string> allowed) =>
    All.Where(o => allowed.Contains(o.Handle))
      .Select(o => new GridFilterOperatorDto
      {
        Handle = o.Handle,
        Label = Localizer.Instance[TextKey(o.Handle)],
        Symbol = o.Symbol
      })
      .ToList();

  public static List<GridFilterOperatorDto> ForField(string fieldType, bool isReference = false, bool isTemporal = false)
  {
    if (isReference)
    {
      return For(Identifying);
    }

    if (fieldType == UiFieldType.Text)
    {
      return For(TextOperators);
    }

    if (fieldType == UiFieldType.Number || isTemporal || fieldType == UiFieldType.Date)
    {
      return For(Ordered);
    }

    if (fieldType == UiFieldType.Checkbox)
    {
      return For(Identity);
    }

    return For(Identifying);
  }

  private static readonly HashSet<string> Identity = [FilterOperator.Equals, FilterOperator.NotEquals];
  private static readonly HashSet<string> Identifying = [FilterOperator.Equals, FilterOperator.NotEquals, FilterOperator.In, FilterOperator.NotIn, FilterOperator.IsNull, FilterOperator.IsNotNull];
  private static readonly HashSet<string> Order = [FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual, FilterOperator.LessThan, FilterOperator.LessThanOrEqual];
  private static readonly HashSet<string> Ordered = [.. Identifying, .. Order];
  private static readonly HashSet<string> TextOperators = [.. FilterOperator.All];

  private static GridFilterOperatorDto Operator(string handle, string? symbol = null) =>
    new() { Handle = handle, Label = UiTexts.English.GetValueOrDefault(TextKey(handle), handle), Symbol = symbol };
}
