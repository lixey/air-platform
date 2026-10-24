namespace Platform.App.Files.Contracts;

[UiContract]
public sealed class FileInfoDto
{
  public long Id { get; set; }
  public required string Name { get; set; }
  public required string ContentType { get; set; }
  public long Size { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Width { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Height { get; set; }

  public DateTimeOffset CreatedAt { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? UploadedBy { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Description { get; set; }

  public int Ver { get; set; }
}
