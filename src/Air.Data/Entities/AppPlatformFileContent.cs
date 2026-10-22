namespace Air.Data.Entities;

public sealed class AppPlatformFileContent
{
  [Key, MaxLength(BaseAppFile.StorageKeyLength)]
  public string Key { get; set; } = string.Empty;

  public long? Length { get; set; }

  public DateTimeOffset CreatedAt { get; set; }
}
