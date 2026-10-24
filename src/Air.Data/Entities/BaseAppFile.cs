namespace Platform.Data.Entities;

public abstract class BaseAppFile : BaseEntity
{
  public const int NameMaxLength = 255;
  public const int ContentTypeMaxLength = 127;
  public const int StorageKeyLength = 43;
  public const int Sha256Length = 64;
  public const int DescriptionMaxLength = 2000;

  [Required, MaxLength(NameMaxLength)]
  public string Name { get; set; } = string.Empty;

  [Required, MaxLength(ContentTypeMaxLength)]
  public string ContentType { get; set; } = string.Empty;

  public long Size { get; set; }

  [Required, MaxLength(StorageKeyLength)]
  public string StorageKey { get; set; } = string.Empty;

  [MaxLength(StorageKeyLength)]
  public string? ThumbnailKey { get; set; }

  [MaxLength(FileStore.KeyMaxLength)]
  public string? Store { get; set; }

  public int SortOrder { get; set; }

  [MaxLength(Sha256Length)]
  public string? Sha256 { get; set; }

  [MaxLength(DescriptionMaxLength)]
  public string? Description { get; set; }

  [Sensitive]
  public string? Metadata { get; set; }

  public int? Width { get; set; }

  public int? Height { get; set; }

  public long? UploadedById { get; set; }
}
