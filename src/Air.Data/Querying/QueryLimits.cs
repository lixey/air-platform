namespace Platform.Data.Querying;

public sealed record QueryLimits
{
  public static QueryLimits Default { get; } = new();

  public int DefaultPageSize { get; init; } = 200;
  public int MaxPageSize { get; init; } = 500;
  public int MaxFilterValues { get; init; } = 1000;
  public int MaxFilterNodes { get; init; } = 200;
  public int MaxFilterDepth { get; init; } = 4;
  public int MaxSortRules { get; init; } = 8;
  public int MaxSearchLength { get; init; } = 128;
  public int MaxSearchWords { get; init; } = 6;
}
