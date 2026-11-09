namespace Platform.App.Agent;

public sealed class AgentSyncExecutionContext
{
  public required string SessionId { get; init; }
  public required string SourceId { get; init; }
  public IReadOnlyList<AgentAttachmentDto> Attachments { get; init; } = [];
  public required string Locale { get; init; }
  public required IServiceProvider Services { get; init; }
  public Func<string, Task>? ReportStatusAsync { get; init; }
}
