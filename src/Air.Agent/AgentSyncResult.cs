namespace Platform.Agent;

public sealed class AgentSyncResult
{
  public string SummaryMarkdown { get; init; } = string.Empty;
  public string? SyncContextJson { get; init; }
  public string? AgentPrompt { get; init; }
  public IReadOnlyList<string> RefreshServices { get; init; } = [];
  public bool RequiresAgentTurn { get; init; }
}
