using Air.Data.Entities;
using DemoApp.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(ProductId))]
[Index(nameof(ProductId), nameof(Sha256))]
[FileRules<OrgMediaStore>(
  MaxBytes = 5 * FileSize.MiB,
  ContentTypes = [FileTypeGroup.Pictures],
  MaxFiles = 12,
  MaxTotalBytes = 30 * FileSize.MiB,
  SourceMaxBytes = 20 * FileSize.MiB,
  Images = FileImages.Optimize,
  ImageMaxSide = 1600,
  ImageQuality = 0.8,
  Cover = FileCover.Auto
)]
public sealed class OrgProductFile : BaseAppFile, IHasTenant, IHasCoverFile
{
  public long TenantId { get; set; }
  public AppTenant Tenant { get; set; } = null!;

  public long ProductId { get; set; }
  public OrgProduct Product { get; set; } = null!;

  public bool IsCover { get; set; }
}
