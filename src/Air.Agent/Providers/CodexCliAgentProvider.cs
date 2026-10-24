namespace Platform.Agent.Providers;

/// <summary>
/// Adapter for OpenAI Codex CLI (`codex exec --json`).
/// </summary>
public sealed class CodexCliAgentProvider : IAgentProvider
{
  public string Handle => "Codex";
  public string Name => "Codex CLI (codex)";
  public bool IsAvailable => false;

  public Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false) =>
    Task.FromResult<IReadOnlyList<AgentModelDto>>(
    [
      new() { Id = "gpt-5.3-codex", Name = "GPT-5.3 Codex", Provider = Handle },
      new() { Id = "o4-mini-high", Name = "OpenAI o4-mini (High)", Provider = Handle }
    ]);

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    await Task.CompletedTask;
    yield return new AgentStreamEventDto
    {
      Type = "error",
      Text = "Codex CLI provider is registered as a future adapter."
    };
  }
}
