namespace Air.Data.Contracts;

/// <summary>
/// A slice of rows for export or unpaged queries, indicating if more rows existed beyond limit.
/// </summary>
public sealed class Slice<TRow>
{
  public required List<TRow> Rows { get; init; }
  public required bool IsTruncated { get; init; }
}
