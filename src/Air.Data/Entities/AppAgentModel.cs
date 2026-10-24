namespace Air.Data.Entities;

[Comment("AI assistant model configuration per provider")]
[Index(nameof(ProviderHandle), nameof(ModelId), IsUnique = true)]
public class AppAgentModel : BaseEntity, IHasIsActive
{
  [Required, MaxLength(64)]
  public string ProviderHandle { get; set; } = "Agy";

  [Required, MaxLength(128)]
  public string ProviderName { get; set; } = "Antigravity CLI (agy)";

  [Required, MaxLength(128)]
  public string ModelId { get; set; } = string.Empty;

  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  public int SortOrder { get; set; }

  public bool IsDefault { get; set; }

  public bool IsForPhotos { get; set; }

  public bool IsActive { get; set; } = true;
}
