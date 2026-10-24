namespace Platform.Agent.Providers;

/// <summary>
/// Adapter for local OpenAI-compatible servers (LM Studio, Ollama, vLLM).
/// </summary>
public sealed class OpenAiCompatibleAgentProvider : IAgentProvider
{
  public string Handle => "LmStudio";
  public string Name => "LM Studio / Local OpenAI";
  public bool IsAvailable => false;

  public Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false) =>
    Task.FromResult<IReadOnlyList<AgentModelDto>>(
    [
      new() { Id = "qwen3-coder-32b", Name = "Qwen 3 Coder 32B (Local)", Provider = Handle }
    ]);

  public async IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    await Task.CompletedTask;
    yield return new AgentStreamEventDto
    {
      Type = "error",
      Text = "LM Studio provider is registered as a future adapter."
    };
  }
}
