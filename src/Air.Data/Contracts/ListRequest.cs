namespace Platform.Data.Contracts;

/// <summary>
/// Standard list request containing pagination, sorting, filters, and fulltext search.
/// </summary>
[ClientOptional]
public class ListRequest
{
  public int Page { get; set; } = 1;

  public int PageSize { get; set; }

  public List<SortRule>? Sort { get; set; }

  public FilterNode? Filter { get; set; }

  public FilterNode? ColumnFilter { get; set; }

  public string? Search { get; set; }

  public List<string>? SearchFields { get; set; }
}
