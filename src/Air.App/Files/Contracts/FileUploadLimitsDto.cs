namespace Platform.App.Files.Contracts;

[UiContract]
public sealed record FileUploadLimitsDto
{
  public List<string> Accept { get; init; } = [];
  public List<string> Extensions { get; init; } = [];
  public long MaxBytes { get; init; }
  public long SourceMaxBytes { get; init; }
  public int MaxFiles { get; init; }
  public long MaxTotalBytes { get; init; }

  [OneOf(typeof(FileImages))]
  public string Images { get; init; } = FileImages.Optimize;

  public int ImageMaxSide { get; init; }
  public double ImageQuality { get; init; }
  public long MaxPixels { get; init; }
  public long ThumbnailMaxBytes { get; init; }
}
