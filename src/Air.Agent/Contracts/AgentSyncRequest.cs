namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentSyncRequest
{
  public string? SessionId { get; set; }
  public string SourceId { get; set; } = string.Empty;
  public string? Message { get; set; }
  public string? SyncFileAttachmentId { get; set; }
  public List<string>? AttachmentIds { get; set; }
  public string? Locale { get; set; }
  public string? Provider { get; set; }
  public string? Model { get; set; }
}
