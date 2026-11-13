namespace Platform.Agent;

public interface IAgentModelSynchronizer
{
  Task<int> SyncFromProvidersAsync(
    PlatformDbContext db,
    CancellationToken ct = default,
    string? providerHandle = null);
}
