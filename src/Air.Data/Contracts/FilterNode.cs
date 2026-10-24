namespace Platform.Data.Contracts;

/// <summary>
/// A node in the filter tree: FilterGroup or FilterRule, with polymorphic kind discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FilterGroup), nameof(FilterGroup))]
[JsonDerivedType(typeof(FilterRule), nameof(FilterRule))]
public abstract class FilterNode
{
  public static FilterGroup All(params FilterNode[] items) => new() { Logic = FilterLogic.And, Items = [.. items] };

  public static FilterGroup Any(params FilterNode[] items) => new() { Logic = FilterLogic.Or, Items = [.. items] };

  public static FilterRule Rule(string field, string op = FilterOperator.Equals, object? value = null) =>
    new() { Field = field, Operator = op, Value = value };
}
