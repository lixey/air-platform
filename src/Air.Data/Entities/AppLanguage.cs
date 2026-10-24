namespace Platform.Data.Entities;

[Comment("Supported platform language reference entity"), Index(nameof(Handle), IsUnique = true), Index(nameof(Code), IsUnique = true), Index(nameof(Name), IsUnique = true)]
public class AppLanguage : BaseDictionaryEntity, IReadOnlyEntity
{
  [Required, MaxLength(16)]
  public string Code { get; set; } = string.Empty;
}
