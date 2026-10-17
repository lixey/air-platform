using System.ComponentModel.DataAnnotations;

namespace Air.Data.Entities;

/// <summary>
/// Base entity for lookup dictionaries (Handle, Name, Description, IsActive, SortOrder).
/// </summary>
public abstract class BaseDictionaryEntity : BaseExplicitIdEntity, IHasIsActive
{
  [Required, MaxLength(128)]
  public string Handle { get; set; } = string.Empty;

  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  [MaxLength(512)]
  public string? Description { get; set; }

  public bool IsActive { get; set; } = true;

  public int SortOrder { get; set; }

  [MaxLength(512)]
  public string? Comment { get; set; }
}
