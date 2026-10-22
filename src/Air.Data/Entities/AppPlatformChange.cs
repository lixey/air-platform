namespace Air.Data.Entities;

/// <summary>
/// One row a managed mutation added, changed, deleted or restored: the change journal, a platform table of the
/// application's own context and database.
/// </summary>
[Index(nameof(TenantId), nameof(Table), nameof(RowId)), Index(nameof(TenantId), nameof(RunId)), Index(nameof(At))]
public sealed class AppPlatformChange : IEntity, IHasTenant
{
  public const int CorrelationIdMaxLength = 128;
  public const int TableMaxLength = 128;
  public const int OperationMaxLength = 16;

  public long Id { get; set; }

  public long TenantId { get; set; }

  public Guid RunId { get; set; }

  public DateTimeOffset At { get; set; }

  public long? UserId { get; set; }

  [MaxLength(CorrelationIdMaxLength)]
  public string? CorrelationId { get; set; }

  [Required, MaxLength(TableMaxLength)]
  public string Table { get; set; } = string.Empty;

  public long RowId { get; set; }

  [Required, MaxLength(OperationMaxLength)]
  public string Operation { get; set; } = string.Empty;

  public string? Before { get; set; }

  public string? After { get; set; }
}
