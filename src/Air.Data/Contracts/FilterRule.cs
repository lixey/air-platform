namespace Air.Data.Contracts;

/// <summary>
/// A single filter condition on a field.
/// </summary>
public sealed class FilterRule : FilterNode
{
  public string Field { get; set; } = string.Empty;

  [OneOf(typeof(FilterOperator))]
  public string Operator { get; set; } = FilterOperator.Equals;

  public object? Value { get; set; }

  public List<PickedValue>? Picked { get; set; }
}
