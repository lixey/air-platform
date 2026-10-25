namespace Platform.Data.Entities;

[Comment("AI assistant provider configuration")]
[Index(nameof(Handle), IsUnique = true)]
public class AppAgentProvider : BaseEntity, IHasIsActive
{
  [Required, MaxLength(64)]
  public string Handle { get; set; } = string.Empty;

  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  public int SortOrder { get; set; }

  public bool IsActive { get; set; } = true;
}
