using DemoApp.Data.Models;

namespace DemoEcom.Data.Models;

[Index(nameof(Handle), IsUnique = true)]
public sealed class EcomDocStatus : BaseDictionaryEntity, IReadOnlyEntity
{
  public long DocTypeId { get; set; }
  public EcomDocType DocType { get; set; } = null!;

  public long ColorId { get; set; }
  public AppColor Color { get; set; } = null!;

  public bool IsEditable { get; set; }

  public bool IsPosted { get; set; }

  public bool IsFinal { get; set; }

  public bool IsCanceled { get; set; }
}
