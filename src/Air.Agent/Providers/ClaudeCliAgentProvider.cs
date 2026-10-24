namespace Platform.Agent.Providers;

/// <summary>
/// Adapter for Claude Code CLI (`claude -p --output-format stream-json`).
/// Ready for plug-and-play activation when selected.
/// </summary>
public sealed class ClaudeCliAgentProvider : IAgentProvider
{
  public string Handle => "Claude";
  public string Name => "Claude CLI (claude)";
  public bool IsAvailable => false;

  public Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false) =>
    Task.FromResult<IReadOnlyList<AgentModelDto>>(
    [
      new() { Id = "claude-opus-4-6", Name = "Claude Opus 4.6", Provider = Handle },
      new() { Id = "claude-sonnet-4-6", Name = "Claude Sonnet 4.6", Provider = Handle }
    ]);

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    await Task.CompletedTask;
    yield return new AgentStreamEventDto
    {
      Type = "error",
      Text = "Claude CLI provider is registered as a future adapter."
    };
  }
}
