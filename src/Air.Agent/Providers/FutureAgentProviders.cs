using System.Runtime.CompilerServices;

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

  public async IAsyncEnumerable<AgentStreamEvent> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    await Task.CompletedTask;
    yield return new AgentStreamEvent
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

  public async IAsyncEnumerable<AgentStreamEvent> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    await Task.CompletedTask;
    yield return new AgentStreamEvent
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

  public async IAsyncEnumerable<AgentStreamEvent> StreamTurnAsync(
    AgentTurnRequest request,
    [EnumeratorCancellation] CancellationToken ct)
  {
    await Task.CompletedTask;
    yield return new AgentStreamEvent
    {
      Type = "error",
      Text = "LM Studio provider is registered as a future adapter."
    };
  }
}
