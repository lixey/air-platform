using System.ComponentModel.DataAnnotations;

namespace DemoOrg.Data.Models;

[Index(nameof(Handle), IsUnique = true), Index(nameof(Code), IsUnique = true)]
public sealed class OrgCurrency : BaseDictionaryEntity, IReadOnlyEntity
{
  [Required, MaxLength(3)]
  public string Code { get; set; } = string.Empty;

  [MaxLength(4)]
  public string? Symbol { get; set; }
}
