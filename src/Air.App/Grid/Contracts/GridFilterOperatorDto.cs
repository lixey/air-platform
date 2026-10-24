namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class GridFilterOperatorDto
{
  [OneOf(typeof(FilterOperator))]
  public string Handle { get; set; } = FilterOperator.Equals;
  public string Label { get; set; } = string.Empty;
  public string? Symbol { get; set; }
}
