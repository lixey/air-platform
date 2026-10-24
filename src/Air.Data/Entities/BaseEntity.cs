namespace Platform.Data.Entities;

/// <summary>
/// Base entity with standard surrogate Id, audit timestamps, and optimistic concurrency version.
/// </summary>
public abstract class BaseEntity : IEntity, IHasTimestamps, IHasVersion
{
  public virtual long Id { get; set; }

  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

  public DateTimeOffset? UpdatedAt { get; set; }

  public int Ver { get; set; } = 1;
}
