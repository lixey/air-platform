using System.ComponentModel.DataAnnotations;

namespace DemoOrg.Data.Models;

[Index(nameof(Handle), IsUnique = true)]
public sealed class OrgCurrency : BaseDictionaryEntity, IReadOnlyEntity
{
  [MaxLength(4)]
  public string? Symbol { get; set; }
}
