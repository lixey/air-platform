namespace Air.Data.Contracts;

/// <summary>
/// A collection of filter nodes combined with AND / OR logic.
/// </summary>
public sealed class FilterGroup : FilterNode
{
  [OneOf(typeof(FilterLogic))]
  public string Logic { get; set; } = FilterLogic.And;

  public List<FilterNode> Items { get; set; } = [];
}
