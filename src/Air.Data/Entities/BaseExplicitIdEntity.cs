namespace Air.Data.Entities;

/// <summary>
/// Base entity for reference and dictionary entities with deterministic IDs assigned explicitly.
/// </summary>
public abstract class BaseExplicitIdEntity : BaseEntity
{
  [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
  public override long Id { get; set; }
}
