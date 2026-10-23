namespace Air.Agent.Providers;

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
