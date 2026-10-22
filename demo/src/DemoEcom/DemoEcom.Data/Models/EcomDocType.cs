namespace DemoEcom.Data.Models;

[Index(nameof(Handle), IsUnique = true)]
public sealed class EcomDocType : BaseDictionaryEntity, IReadOnlyEntity
{
  [Required, MaxLength(4)]
  public string Prefix { get; set; } = string.Empty;
}
