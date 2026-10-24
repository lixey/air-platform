namespace Platform.Data.Contracts;

/// <summary>
/// Sorting rule for a column/field.
/// </summary>
public sealed class SortRule
{
  public string Field { get; set; } = string.Empty;

  [OneOf(typeof(SortDirection))]
  public string Direction { get; set; } = SortDirection.Asc;

  public static SortRule Asc(string field) => new() { Field = field, Direction = SortDirection.Asc };
  public static SortRule Desc(string field) => new() { Field = field, Direction = SortDirection.Desc };
}
