namespace Platform.Agent;

public interface IAgentSyncProvider
{
  Task<IReadOnlyList<AgentSyncSourceDto>> GetSourcesAsync(IServiceProvider services, CancellationToken ct = default);

  Task<AgentSyncResult> ExecuteSyncAsync(AgentSyncExecutionContext context, CancellationToken ct = default);
}
