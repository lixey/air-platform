namespace Platform.Data.Entities;

/// <summary>
/// Persistent memory and rules entry for the built-in AI assistant (mandatory rules, seeded domain knowledge, and learned facts/preferences).
/// </summary>
[Index(nameof(TenantId), nameof(UserId), nameof(IsActive))]
public class AppAgentMemory : BaseEntity, IHasIsActive, IInheritedByChildTenants
{
  public long? TenantId { get; set; }

  public long? UserId { get; set; }

  [MaxLength(64)]
  public string Kind { get; set; } = AgentMemoryKinds.Rule;

  [MaxLength(64)]
  public string? Module { get; set; }

  [MaxLength(200)]
  public string Title { get; set; } = string.Empty;

  public string Content { get; set; } = string.Empty;

  public int Priority { get; set; } = 100;

  public bool IsPinned { get; set; }

  public bool IsActive { get; set; } = true;

  [NoAudit]
  public int HitCount { get; set; }

  [NoAudit]
  public DateTimeOffset? LastUsedAt { get; set; }
}
