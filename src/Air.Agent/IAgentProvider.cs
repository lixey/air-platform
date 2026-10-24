namespace Platform.Agent;

public interface IAgentProvider
{
  string Handle { get; }
  string Name { get; }
  bool IsAvailable { get; }
  Task<IReadOnlyList<AgentModelDto>> GetModelsAsync(CancellationToken ct = default, bool forceRefresh = false) =>
    Task.FromResult<IReadOnlyList<AgentModelDto>>([]);
  IAsyncEnumerable<AgentStreamEventDto> StreamTurnAsync(AgentTurnRequest request, CancellationToken ct);
}
