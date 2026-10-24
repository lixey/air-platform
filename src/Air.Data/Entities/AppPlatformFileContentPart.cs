namespace Platform.Data.Entities;

[PrimaryKey(nameof(Key), nameof(Index))]
public sealed class AppPlatformFileContentPart
{
  public const int PartBytes = 1024 * 1024;

  [MaxLength(BaseAppFile.StorageKeyLength)]
  public string Key { get; set; } = string.Empty;

  public int Index { get; set; }

  [Required]
  public byte[] Bytes { get; set; } = [];
}
