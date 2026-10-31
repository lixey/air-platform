using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Platform.Data.Internals;

internal sealed class MutationRunEntryState
{
  public required EntityEntry Entry { get; init; }
  public required Type ClrType { get; init; }
  public required string Table { get; init; }
  public long TenantId { get; set; }
  public long RowId { get; set; }
  public required string Operation { get; set; }
  public bool IsAdded { get; init; }
  public bool IsDeleted { get; set; }
  public Dictionary<string, object?> FirstBefore { get; } = new(StringComparer.Ordinal);
  public Dictionary<string, object?> LatestAfter { get; } = new(StringComparer.Ordinal);
  public HashSet<string> TouchedProperties { get; } = new(StringComparer.Ordinal);
}
