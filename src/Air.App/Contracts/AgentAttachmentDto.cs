namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentAttachmentDto
{
  public string Id { get; set; } = string.Empty;
  public string FileName { get; set; } = string.Empty;
  public string ContentType { get; set; } = string.Empty;
  public long Size { get; set; }
  public string PreviewUrl { get; set; } = string.Empty;

  [JsonIgnore]
  public string LocalPath { get; set; } = string.Empty;

  [JsonIgnore]
  public string? Sha256 { get; set; }
}
