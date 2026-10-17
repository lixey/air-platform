namespace DemoEcom.Data.Models;

/// <summary>
/// A link between two documents, or between their lines: the source is the earlier document, the target the one entered
/// on it, with the quantity or the amount the target takes of the source when it takes a part. It goes with either end.
/// </summary>
[Index(nameof(TenantId), nameof(SourceDocId)), Index(nameof(TenantId), nameof(SourceLineId))]
[Index(nameof(TenantId), nameof(TargetDocId)), Index(nameof(TenantId), nameof(TargetLineId))]
public sealed class EcomDocLink : AppBaseModel
{
  public long SourceDocId { get; set; }
  public EcomDoc SourceDoc { get; set; } = null!;

  public long? SourceLineId { get; set; }
  public EcomDocLine? SourceLine { get; set; }

  public long TargetDocId { get; set; }
  public EcomDoc TargetDoc { get; set; } = null!;

  public long? TargetLineId { get; set; }
  public EcomDocLine? TargetLine { get; set; }

  [Precision(18, 3)]
  public decimal? Qty { get; set; }

  [Precision(18, 2)]
  public decimal? Amount { get; set; }
}
